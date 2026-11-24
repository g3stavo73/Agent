use serde_json::{json, Value};
use std::io::Write;
use std::process::{Command, Stdio};
use std::thread;

fn valid_request(request_id: &str) -> String {
    json!({
        "requestId": request_id,
        "operation": "compile_aisto",
        "agentName": "Agent Integration Test",
        "configuration": {
            "schemaVersion": 1,
            "domain": "Backend engineering",
            "objective": "Verify the worker JSONL protocol",
            "instructions": "Return a deterministic context",
            "projectContext": "Integration test process",
            "documentationSources": ["https://doc.rust-lang.org/"],
            "responseStyle": "StepByStep"
        },
        "capabilities": {
            "canReadRepositories": true,
            "canWriteRepositories": false,
            "canBrowseWeb": false,
            "useSemanticNavigation": true,
            "preferUpdatedDocumentation": true,
            "applyArchitectureRules": true
        }
    })
    .to_string()
}

fn run_worker(lines: &[String]) -> Vec<Value> {
    let mut child = Command::new(env!("CARGO_BIN_EXE_aisto-worker"))
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .expect("AiSTo worker process should start");

    {
        let stdin = child.stdin.as_mut().expect("worker stdin should be piped");
        for line in lines {
            writeln!(stdin, "{line}").expect("request should be written to worker stdin");
        }
    }
    drop(child.stdin.take());

    let output = child
        .wait_with_output()
        .expect("worker process should finish after stdin reaches EOF");
    assert!(
        output.status.success(),
        "worker exited unsuccessfully: {}",
        String::from_utf8_lossy(&output.stderr)
    );

    String::from_utf8(output.stdout)
        .expect("worker stdout must be UTF-8")
        .lines()
        .map(|line| serde_json::from_str(line).expect("every stdout line must be a JSON response"))
        .collect()
}

#[test]
fn worker_correlates_schema_errors_and_keeps_processing_following_requests() {
    let lines = vec![
        r#"{"requestId":"bad-shape","operation":"compile_aisto"}"#.to_owned(),
        valid_request("valid-after-error"),
        json!({
            "requestId": "unsupported-operation",
            "operation": "not_supported",
            "agentName": "Agent Integration Test",
            "configuration": {
                "schemaVersion": 1,
                "domain": "Backend engineering",
                "objective": "Test invalid operation",
                "instructions": "",
                "projectContext": "",
                "documentationSources": [],
                "responseStyle": "Concise"
            },
            "capabilities": {
                "canReadRepositories": true,
                "canWriteRepositories": false,
                "canBrowseWeb": false,
                "useSemanticNavigation": false,
                "preferUpdatedDocumentation": false,
                "applyArchitectureRules": false
            }
        })
        .to_string(),
        valid_request("valid-after-operation-error"),
    ];

    let responses = run_worker(&lines);
    assert_eq!(responses.len(), 4);

    assert_eq!(responses[0]["requestId"].as_str(), Some("bad-shape"));
    assert_eq!(responses[0]["ok"].as_bool(), Some(false));
    assert!(responses[0]["error"].as_str().unwrap().contains("Invalid request payload"));

    assert_eq!(responses[1]["requestId"].as_str(), Some("valid-after-error"));
    assert_eq!(responses[1]["ok"].as_bool(), Some(true));

    assert_eq!(responses[2]["requestId"].as_str(), Some("unsupported-operation"));
    assert_eq!(responses[2]["ok"].as_bool(), Some(false));

    assert_eq!(responses[3]["requestId"].as_str(), Some("valid-after-operation-error"));
    assert_eq!(responses[3]["ok"].as_bool(), Some(true));
}

#[test]
fn independent_worker_processes_handle_requests_concurrently() {
    let handles: Vec<_> = (0..4)
        .map(|index| {
            thread::spawn(move || {
                let response = run_worker(&[valid_request(&format!("parallel-process-{index}"))]);
                assert_eq!(response.len(), 1);
                assert_eq!(
                    response[0]["requestId"].as_str(),
                    Some(format!("parallel-process-{index}").as_str())
                );
                assert_eq!(response[0]["ok"].as_bool(), Some(true));
            })
        })
        .collect();

    for handle in handles {
        handle.join().expect("parallel worker process test panicked");
    }
}
