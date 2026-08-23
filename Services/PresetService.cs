using System.Diagnostics;
using System.Text.Json;
using HappyPhoton.Models;

namespace HappyPhoton.Services;

public class PresetService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private string? _presetsDirectory;
    private readonly List<Preset> _userPresets = new();
    private IReadOnlyList<Preset> _userPresetSnapshot = Array.Empty<Preset>();
    private bool _initialized;

    public PresetService()
    {
    }

    public PresetService(string presetsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetsDirectory);
        _presetsDirectory = presetsDirectory;
    }

    public IReadOnlyList<Preset> AllPresets => UserPresets;

    public IReadOnlyList<Preset> UserPresets => _userPresetSnapshot;

    public event EventHandler? PresetsChanged;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        var directory = _presetsDirectory ?? throw new InvalidOperationException(
            "PresetService has no directory. Call UseDirectoryAsync first.");
        Directory.CreateDirectory(directory);
        var loadedPresets = new List<Preset>();
        var loadedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var file = DeserializePresetFile(json, path);
                if (file == null || string.IsNullOrWhiteSpace(file.Id) ||
                    string.IsNullOrWhiteSpace(file.Name) || file.Settings == null)
                {
                    Debug.WriteLine($"Skipping invalid preset file: {path}");
                    continue;
                }

                if (!loadedIds.Add(file.Id))
                {
                    Debug.WriteLine($"Skipping duplicate preset id '{file.Id}' in: {path}");
                    continue;
                }

                file.Settings.Curve ??= new CurveData();
                file.Settings.Curve.BuildLookupTable();
                loadedPresets.Add(file.ToPreset());
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Skipping unreadable preset file '{path}': {exception.Message}");
            }
        }

        _userPresets.Clear();
        _userPresets.AddRange(loadedPresets);
        RefreshPresetSnapshot();
        _initialized = true;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task UseDirectoryAsync(string presetsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetsDirectory);
        var normalized = Path.GetFullPath(presetsDirectory);
        if (_initialized && string.Equals(
                _presetsDirectory,
                normalized,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            return;
        }

        _presetsDirectory = normalized;
        _initialized = false;
        await InitializeAsync();
    }

    public Preset? GetById(string id)
    {
        return _userPresets.FirstOrDefault(preset => preset.Id == id);
    }

    public Preset? FindUserPresetByName(string name)
    {
        return _userPresets.FirstOrDefault(preset =>
            string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Preset> SaveUserPresetAsync(
        string name,
        EditSettings source,
        string? overwriteId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);

        var trimmedName = name.Trim();
        var existingIndex = overwriteId == null
            ? -1
            : _userPresets.FindIndex(preset => preset.Id == overwriteId);

        if (overwriteId != null && existingIndex < 0)
        {
            throw new ArgumentException("The preset to overwrite does not exist.", nameof(overwriteId));
        }

        var id = overwriteId ?? $"user_{Guid.NewGuid():N}";
        var settings = CreatePresetSettings(source);
        var file = new UserPresetFile
        {
            Id = id,
            Name = trimmedName,
            Settings = settings
        };

        Directory.CreateDirectory(RequireDirectory());
        var path = GetPresetPath(id);
        var json = JsonSerializer.Serialize(file, JsonOptions);
        await File.WriteAllTextAsync(path, json);

        var preset = file.ToPreset();
        if (existingIndex >= 0)
        {
            _userPresets[existingIndex] = preset;
        }
        else
        {
            _userPresets.Add(preset);
        }

        RefreshPresetSnapshot();
        PresetsChanged?.Invoke(this, EventArgs.Empty);
        return preset;
    }

    public async Task RenameUserPresetAsync(string id, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var index = _userPresets.FindIndex(preset => preset.Id == id);
        if (index < 0)
        {
            return;
        }

        var current = _userPresets[index];
        var file = new UserPresetFile
        {
            Id = current.Id,
            Name = newName.Trim(),
            Settings = current.Settings.Clone()
        };

        var json = JsonSerializer.Serialize(file, JsonOptions);
        await File.WriteAllTextAsync(GetPresetPath(id), json);
        _userPresets[index] = file.ToPreset();
        RefreshPresetSnapshot();
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task DeleteUserPresetAsync(string id)
    {
        var index = _userPresets.FindIndex(preset => preset.Id == id);
        if (index < 0)
        {
            return Task.CompletedTask;
        }

        return DeleteUserPresetCoreAsync(id, index);
    }

    private async Task DeleteUserPresetCoreAsync(string id, int index)
    {
        await Task.Run(() => File.Delete(GetPresetPath(id)));
        _userPresets.RemoveAt(index);
        RefreshPresetSnapshot();
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPresetSnapshot()
    {
        _userPresets.Sort((a, b) =>
            string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _userPresetSnapshot = Array.AsReadOnly(_userPresets.ToArray());
    }

    private string GetPresetPath(string id) =>
        Path.Combine(RequireDirectory(), $"{id}.json");

    private string RequireDirectory() => _presetsDirectory ??
        throw new InvalidOperationException("PresetService is not bound to a directory.");

    private static EditSettings CreatePresetSettings(EditSettings source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Version != EditSettings.CurrentVersion)
        {
            throw new NotSupportedException(
                $"Edit settings version {source.Version} is not supported.");
        }
        var settings = source.Clone();
        settings.Rotation = 0;
        settings.HorizonRotation = 0;
        settings.Crop = null;
        if (settings.Effects?.HasActivePixels != true)
        {
            settings.Effects = null;
        }
        if (settings.Mixer?.HasActivePixels != true)
        {
            settings.Mixer = null;
        }
        settings.AppliedPresetId = null;
        settings.RawProfile = null;
        return settings;
    }

    private static UserPresetFile? DeserializePresetFile(string json, string path)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Preset root must be an object.");
        }
        if (!root.TryGetProperty("version", out var fileVersionElement))
        {
            throw new JsonException("Preset version is required.");
        }
        var fileVersion = ReadVersion(fileVersionElement, "preset");
        if (fileVersion != UserPresetFile.CurrentVersion)
        {
            Debug.WriteLine(
                $"Skipping preset with unsupported version {fileVersion}: {path}");
            return null;
        }
        if (!root.TryGetProperty("settings", out var settingsElement))
        {
            return null;
        }
        if (settingsElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Preset settings must be an object.");
        }

        var settings = EditSettingsJson.Deserialize(
            settingsElement.GetRawText(),
            out var wasClamped);
        // Camera profiles are image-specific and never transfer through a
        // preset file, including a hand-edited one.
        settings.RawProfile = null;
        if (wasClamped)
        {
            Debug.WriteLine($"Clamped out-of-range preset settings: {path}");
        }

        return new UserPresetFile
        {
            Version = UserPresetFile.CurrentVersion,
            Id = ReadOptionalString(root, "id"),
            Name = ReadOptionalString(root, "name"),
            Settings = settings
        };
    }

    private static string ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            return "";
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Preset {propertyName} must be a string.");
        }
        return element.GetString() ?? "";
    }

    private static int ReadVersion(JsonElement element, string name)
    {
        if (!element.TryGetInt32(out var version))
        {
            throw new JsonException($"{name} version must be an integer.");
        }
        return version;
    }
}
