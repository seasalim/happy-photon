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

    private readonly string _presetsDirectory;
    private readonly List<Preset> _userPresets = new();
    private bool _initialized;

    public PresetService(string presetsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetsDirectory);
        _presetsDirectory = presetsDirectory;
    }

    public IReadOnlyList<Preset> AllPresets => UserPresets;

    public IReadOnlyList<Preset> UserPresets => _userPresets
        .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public event EventHandler? PresetsChanged;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        Directory.CreateDirectory(_presetsDirectory);
        var loadedPresets = new List<Preset>();
        var loadedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(_presetsDirectory, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var file = JsonSerializer.Deserialize<UserPresetFile>(json, JsonOptions);
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
        _initialized = true;
        PresetsChanged?.Invoke(this, EventArgs.Empty);
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

        Directory.CreateDirectory(_presetsDirectory);
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
        PresetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private string GetPresetPath(string id) => Path.Combine(_presetsDirectory, $"{id}.json");

    private static EditSettings CreatePresetSettings(EditSettings source)
    {
        var settings = source.Clone();
        settings.Rotation = 0;
        settings.HorizonRotation = 0;
        settings.Crop = null;
        settings.AppliedPresetId = null;
        return settings;
    }
}
