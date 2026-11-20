use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::fmt::Write as _;
use std::io::{self, BufRead, BufReader, BufWriter, Write as _};

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct WorkerRequest {
    request_id: String,
    operation: String,
    agent_name: String,
    configuration: AiStoConfiguration,
    capabilities: AgentCapabilities,
}

#[derive(Debug, Deserialize)]
#[serde(rename_all = "camelCase")]
struct AiStoConfiguration {
    schema_version: Option<u32>,
    domain: String,
    objective: String,
    instructions: String,
    project_context: String,
    documentation_sources: Vec<String>,
    response_style: String,
}

#[derive(Debug, Deserialize, Serialize)]
#[serde(rename_all = "camelCase")]
struct AgentCapabilities {
    can_read_repositories: bool,
    can_write_repositories: bool,
    can_browse_web: bool,
    use_semantic_navigation: bool,
    prefer_updated_documentation: bool,
    apply_architecture_rules: bool,
}

#[derive(Debug, Serialize)]
#[serde(rename_all = "camelCase")]
struct WorkerResponse {
    request_id: String,
    ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    system_instruction: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    documentation_sources: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    requested_capabilities: Option<AgentCapabilities>,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
}

fn compile(request: WorkerRequest) -> WorkerResponse {
    let id = request.request_id.clone();
    if request.operation != "compile_aisto" {
        return failure(id, "Unsupported worker operation.");
    }
    if request.agent_name.trim().is_empty() || request.agent_name.chars().count() > 80 {
        return failure(id, "Agent name must contain between 1 and 80 characters.");
    }
    let config = request.configuration;
    if config.schema_version.unwrap_or(1) != 1 {
        return failure(id, "Unsupported AiSTo configuration schema version.");
    }
    if config.domain.trim().is_empty() || config.domain.chars().count() > 80 {
        return failure(id, "Domain must contain between 1 and 80 characters.");
    }
    if config.objective.chars().count() > 1600
        || config.instructions.chars().count() > 8000
        || config.project_context.chars().count() > 5000
    {
        return failure(id, "An AiSTo text field exceeds its allowed size.");
    }
    if !["StepByStep", "Concise", "Detailed", "CriticalReview"]
        .contains(&config.response_style.as_str())
    {
        return failure(id, "Unsupported response style.");
    }

    if request.capabilities.can_write_repositories && !request.capabilities.can_read_repositories {
        return failure(id, "Repository write requires repository read to be enabled.");
    }

    if config.documentation_sources.len() > 20 {
        return failure(id, "No more than 20 documentation sources may be configured.");
    }

    // Keep input order while making duplicate detection constant-time on average.
    // URL parsing also normalizes the host, so equivalent host casing shares a key.
    let mut sources = Vec::with_capacity(config.documentation_sources.len());
    let mut source_keys = HashSet::with_capacity(config.documentation_sources.len());
    for candidate in config.documentation_sources {
        let trimmed = candidate.trim();
        if trimmed.chars().count() > 2048 {
            return failure(id, "Documentation sources must be HTTPS URLs without credentials and no longer than 2048 characters.");
        }
        let Some(parsed) = parse_https_reference(trimmed) else {
            return failure(id, "Documentation sources must be valid HTTPS URLs without embedded credentials.");
        };
        let normalized = parsed.to_string();
        // url::Url canonicalizes the host while preserving case-sensitive paths.
        if source_keys.insert(normalized.clone()) {
            sources.push(normalized);
        }
    }

    let style = match config.response_style.as_str() {
        "Concise" => "Prioritize concise answers and omit unnecessary exposition.",
        "Detailed" => "Explain relevant details, assumptions, and trade-offs.",
        "CriticalReview" => "Review critically: identify risks, contradictions, missing requirements, and alternatives.",
        _ => "Work step by step and explain the reasoning that is useful to the user.",
    };

    // Reserve once based on the actual input sizes to avoid repeated reallocations
    // while assembling the prompt for larger project context fields.
    let estimated_capacity = 512
        + request.agent_name.len()
        + config.domain.len()
        + config.objective.len()
        + config.instructions.len()
        + config.project_context.len();
    let mut instruction = String::with_capacity(estimated_capacity);
    instruction.push_str("# Agent identity\nDisplay name: ");
    instruction.push_str(request.agent_name.trim());
    instruction.push_str("\nUse this configured display name in user-facing communication. It is a product persona, not a separate Microsoft or GitHub account.\n\n");
    instruction.push_str("# Agent specialization context (AiSTo)\n\n## Domain\n");
    instruction.push_str(config.domain.trim());
    instruction.push_str("\n\n## Objective\n");
    instruction.push_str(config.objective.trim());
    instruction.push_str("\n\n## Operating instructions\n");
    instruction.push_str(config.instructions.trim());
    instruction.push_str("\n\n## Project context\n");
    instruction.push_str(config.project_context.trim());
    instruction.push_str("\n\n## Response style\n");
    instruction.push_str(style);
    instruction.push_str("\n\n## Requested capabilities (not grants)\n");
    let _ = writeln!(
        instruction,
        "- Repository read requested: {}",
        request.capabilities.can_read_repositories
    );
    let _ = writeln!(
        instruction,
        "- Repository write requested: {}",
        request.capabilities.can_write_repositories
    );
    let _ = writeln!(
        instruction,
        "- Web research requested: {}",
        request.capabilities.can_browse_web
    );
    let _ = writeln!(
        instruction,
        "- Semantic navigation requested: {}",
        request.capabilities.use_semantic_navigation
    );
    let _ = writeln!(
        instruction,
        "- Prefer updated documentation: {}",
        request.capabilities.prefer_updated_documentation
    );
    let _ = writeln!(
        instruction,
        "- Apply architecture rules: {}",
        request.capabilities.apply_architecture_rules
    );
    instruction.push_str("\nCapability preferences do not grant access. The host must verify user authorization and require explicit approval before repository writes.\n");

    WorkerResponse {
        request_id: id,
        ok: true,
        system_instruction: Some(instruction),
        documentation_sources: Some(sources),
        requested_capabilities: Some(request.capabilities),
        error: None,
    }
}

fn parse_https_reference(value: &str) -> Option<url::Url> {
    let parsed = url::Url::parse(value.trim()).ok()?;
    if parsed.scheme() != "https"
        || parsed.host().is_none()
        || !parsed.username().is_empty()
        || parsed.password().is_some()
    {
        return None;
    }
    Some(parsed)
}

#[cfg(test)]
fn is_https_reference(value: &str) -> bool {
    parse_https_reference(value).is_some()
}

fn failure(id: String, message: &str) -> WorkerResponse {
    WorkerResponse {
        request_id: id,
        ok: false,
        system_instruction: None,
        documentation_sources: None,
        requested_capabilities: None,
        error: Some(message.to_owned()),
    }
}

fn main() {
    let stdin = io::stdin();
    let stdout = io::stdout();
    let mut input = BufReader::new(stdin.lock());
    let mut output = BufWriter::new(stdout.lock());
    // Reuse this allocation across JSON Lines requests instead of allocating a new
    // String for every input line. This worker is intentionally long-lived.
    let mut line = String::with_capacity(16 * 1024);

    loop {
        line.clear();
        match input.read_line(&mut line) {
            Ok(0) => break,
            Ok(_) => {
                let response = match serde_json::from_str::<WorkerRequest>(&line) {
                    Ok(request) => compile(request),
                    Err(error) => failure(String::new(), &format!("Invalid request JSON: {error}")),
                };

                if let Err(error) = serde_json::to_writer(&mut output, &response) {
                    eprintln!("stdout JSON error: {error}");
                    break;
                }
                if let Err(error) = output.write_all(b"\n").and_then(|_| output.flush()) {
                    eprintln!("stdout write error: {error}");
                    break;
                }
            }
            Err(error) => {
                eprintln!("stdin read error: {error}");
                break;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample() -> WorkerRequest {
        WorkerRequest {
            request_id: "test-1".into(),
            operation: "compile_aisto".into(),
            agent_name: "Agent Dev".into(),
            configuration: AiStoConfiguration {
                schema_version: Some(1),
                domain: "Rust backend".into(),
                objective: "Build reliable services".into(),
                instructions: "Prefer explicit errors".into(),
                project_context: "Agent mobile app".into(),
                documentation_sources: vec!["https://doc.rust-lang.org/".into()],
                response_style: "StepByStep".into(),
            },
            capabilities: AgentCapabilities {
                can_read_repositories: true,
                can_write_repositories: false,
                can_browse_web: true,
                use_semantic_navigation: true,
                prefer_updated_documentation: true,
                apply_architecture_rules: true,
            },
        }
    }

    #[test]
    fn compiles_context_without_granting_capabilities() {
        let response = compile(sample());
        assert!(response.ok);
        let instruction = response.system_instruction.unwrap();
        assert!(instruction.contains("Capability preferences do not grant access"));
        assert_eq!(response.documentation_sources.unwrap().len(), 1);
    }

    #[test]
    fn rejects_unsupported_operations() {
        let mut request = sample();
        request.operation = "execute_codex".into();
        assert!(!compile(request).ok);
    }

    #[test]
    fn rejects_invalid_documentation_urls_instead_of_silently_dropping_them() {
        let mut request = sample();
        request.configuration.documentation_sources.push("http://insecure.example".into());
        assert!(!compile(request).ok);
    }

    #[test]
    fn rejects_more_than_twenty_documentation_sources() {
        let mut request = sample();
        request.configuration.documentation_sources = (0..21)
            .map(|index| format!("https://docs{index}.example/"))
            .collect();
        assert!(!compile(request).ok);
    }

    #[test]
    fn accepts_https_localhost_and_ip_hosts() {
        assert!(is_https_reference("https://localhost:8443/docs"));
        assert!(is_https_reference("https://127.0.0.1/docs"));
    }

    #[test]
    fn rejects_write_without_read_permission_preference() {
        let mut request = sample();
        request.capabilities.can_read_repositories = false;
        request.capabilities.can_write_repositories = true;
        assert!(!compile(request).ok);
    }

    #[test]
    fn deduplicates_normalized_documentation_sources_without_reordering() {
        let mut request = sample();
        request.configuration.documentation_sources = vec![
            "https://DOC.RUST-LANG.ORG/book/".into(),
            "https://doc.rust-lang.org/book/".into(),
            "https://docs.github.com/".into(),
        ];
        let response = compile(request);
        assert!(response.ok);
        let sources = response.documentation_sources.unwrap();
        assert_eq!(sources.len(), 2);
        assert_eq!(sources[0], "https://doc.rust-lang.org/book/");
        assert_eq!(sources[1], "https://docs.github.com/");
    }

    #[test]
    fn rejects_urls_with_embedded_credentials() {
        assert!(!is_https_reference("https://user:secret@example.com/docs"));
    }

    #[test]
    fn reserves_and_returns_context_for_long_but_valid_project_details() {
        let mut request = sample();
        request.configuration.project_context = "context ".repeat(500);
        let response = compile(request);
        assert!(response.ok);
        assert!(response.system_instruction.unwrap().contains("context context"));
    }
}
