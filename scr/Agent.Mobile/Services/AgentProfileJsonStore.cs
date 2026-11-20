using System.Text.Json;
using Agent.Mobile.Models;
using Microsoft.Maui.Storage;

namespace Agent.Mobile.Services;

/// <summary>
/// Temporary local store retained from the frontend prototype. Agent.Api now
/// provides remote profile persistence, but this client remains local until the
/// MAUI MSAL and API synchronization layer is integrated.
/// </summary>
public sealed class AgentProfileJsonStore : IAgentProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic", ".heif", ".avif"
    };

    private const long MaxAvatarBytes = 20 * 1024 * 1024;
    private readonly SemaphoreSlim _profilesGate = new(1, 1);

    private string ProfilesFilePath => Path.Combine(FileSystem.AppDataDirectory, "agents.json");
    private string AvatarDirectory => Path.Combine(FileSystem.AppDataDirectory, "agent-avatars");

    public async Task<List<AgentProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _profilesGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(ProfilesFilePath))
                return new List<AgentProfile>();

            await using var stream = new FileStream(
                ProfilesFilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16 * 1024, useAsync: true);

            var profiles = await JsonSerializer.DeserializeAsync<List<AgentProfile>>(
                stream, JsonOptions, cancellationToken) ?? new List<AgentProfile>();

            var validProfiles = new List<AgentProfile>(profiles.Count);
            foreach (var profile in profiles)
            {
                if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
                    continue;

                profile.Name = profile.Name.Trim();
                if (profile.Name.Length > 40)
                    profile.Name = profile.Name[..40];
                profile.Description = (profile.Description ?? string.Empty).Trim();
                if (profile.Description.Length > 1200)
                    profile.Description = profile.Description[..1200];

                if (!IsManagedAvatar(profile.AvatarPath))
                    profile.AvatarPath = null;

                // Codex remains the only engine. AiSTo is a configuration layer over Codex.
                profile.Engine = "codex";
                profile.AiSto?.Normalize();
                validProfiles.Add(profile);
            }

            return validProfiles;
        }
        finally
        {
            _profilesGate.Release();
        }
    }

    public async Task SaveAsync(
        IReadOnlyCollection<AgentProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        await _profilesGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(FileSystem.AppDataDirectory);
            var temporaryPath = ProfilesFilePath + ".tmp";

            try
            {
                await using (var stream = new FileStream(
                    temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 16 * 1024, useAsync: true))
                {
                    await JsonSerializer.SerializeAsync(stream, profiles, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, ProfilesFilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }
        finally
        {
            _profilesGate.Release();
        }
    }

    public async Task<string> SaveAvatarAsync(
        Stream image,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.CanRead)
            throw new InvalidOperationException("Não foi possível ler a imagem selecionada.");

        var extension = Path.GetExtension(fileName);
        if (!AllowedImageExtensions.Contains(extension))
            extension = ExtensionFromContentType(contentType);

        if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
            throw new InvalidDataException("Formato não reconhecido. Escolha uma imagem JPG, PNG, GIF, WebP, BMP, HEIF ou AVIF.");

        Directory.CreateDirectory(AvatarDirectory);
        var destination = Path.Combine(AvatarDirectory, $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");

        try
        {
            var signature = new byte[16];
            var signatureLength = 0;
            var buffer = new byte[64 * 1024];
            long totalBytes = 0;

            await using var target = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 64 * 1024, useAsync: true);

            int read;
            while ((read = await image.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytes += read;
                if (totalBytes > MaxAvatarBytes)
                    throw new InvalidDataException("A foto do agente deve ter no máximo 20 MB.");

                var remainingSignature = signature.Length - signatureLength;
                if (remainingSignature > 0)
                {
                    var copyLength = Math.Min(remainingSignature, read);
                    Array.Copy(buffer, 0, signature, signatureLength, copyLength);
                    signatureLength += copyLength;
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (!LooksLikeSupportedImage(signature.AsSpan(0, signatureLength), extension))
                throw new InvalidDataException("O conteúdo do arquivo não corresponde a uma imagem JPG, PNG, GIF, WebP, BMP, HEIF ou AVIF válida.");

            await target.FlushAsync(cancellationToken);
            return destination;
        }
        catch
        {
            if (File.Exists(destination))
                File.Delete(destination);
            throw;
        }
    }

    public void DeleteAvatar(string? path)
    {
        if (!IsManagedAvatar(path))
            return;

        try
        {
            File.Delete(Path.GetFullPath(path!));
        }
        catch
        {
            // Best-effort cleanup; never let file cleanup remove a saved profile.
        }
    }

    private bool IsManagedAvatar(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetFullPath(AvatarDirectory).TrimEnd(Path.DirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.Ordinal) && File.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeSupportedImage(ReadOnlySpan<byte> bytes, string extension)
    {
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 6 &&
                   (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8));

        if (extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8);

        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 2 && bytes[..2].SequenceEqual("BM"u8);

        if (extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".heif", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8);

        if (extension.Equals(".avif", StringComparison.OrdinalIgnoreCase))
            return bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual("ftyp"u8) &&
                   (bytes.Slice(8, 4).SequenceEqual("avif"u8) || bytes.Slice(8, 4).SequenceEqual("avis"u8));

        return false;
    }

    private static string? ExtensionFromContentType(string? contentType) => contentType?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        "image/heic" => ".heic",
        "image/heif" => ".heif",
        "image/avif" => ".avif",
        _ => null
    };
}
