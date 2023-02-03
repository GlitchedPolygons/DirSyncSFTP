/*
    DirSyncSFTP
    Copyright (C) 2023  Raphael Beck

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml;

namespace DirSyncSFTP;

using System;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using GlitchedPolygons.ExtensionMethods;

/// <summary>
/// Drop-in replacement for the <see cref="PlayerPrefs"/> class. Uses a JSON file inside <see cref="Application.persistentDataPath"/> as the underlying player preferences storage technology. 
/// </summary>
public class JsonPrefs
{
    private string directory;
    private string jsonFileName;
    private string jsonFilePath;
    private JsonSerializerOptions? jsonFormatting;

    private IDictionary<string, string> prefs = new ConcurrentDictionary<string, string>();

    private static readonly IDictionary<string, JsonPrefs> INSTANCES = new ConcurrentDictionary<string, JsonPrefs>();

    /// <summary>
    /// Pre-warms a set of JsonPrefs files by loading them already into RAM.
    /// </summary>
    /// <param name="jsonFiles">All the .json files to pre-load.</param>
    /// <param name="loadAfterConstruction">[OPTIONAL] Should <see cref="Load"/> be called after the first call to <see cref="FromFile"/> (after the <see cref="JsonPrefs"/> instance construction)? Default: <c>true</c></param>
    /// <param name="jsonFormatting">[OPTIONAL] How the JSON should be formatted when written out to a file inside <c>AppData\LocalLow</c>. Default: <see cref="Formatting.Indented"/></param>
    public static void Warmup(bool loadAfterConstruction = true, JsonSerializerOptions? jsonFormatting = null, params string[] jsonFiles)
    {
        foreach (string jsonFile in jsonFiles)
        {
            _ = FromFile(jsonFile, loadAfterConstruction, jsonFormatting);
        }
    }

    /// <summary>
    /// Gets a <see cref="JsonPrefs"/> context instance for a given <paramref name="jsonFileName"/> (which must end in "<c>.json</c>").
    /// </summary>
    /// <param name="jsonFileName">Name of the JSON file that contains the <see cref="JsonPrefs"/>. Must end in "<c>.json</c>"</param>
    /// <param name="loadAfterConstruction">[OPTIONAL] Should <see cref="Load"/> be called after the first call to <see cref="FromFile"/> (after the <see cref="JsonPrefs"/> instance construction)? Default: <c>true</c></param>
    /// <param name="jsonFormatting">[OPTIONAL] How the JSON should be formatted when written out to a file inside <c>AppData\LocalLow</c>. Default: <see cref="Formatting.Indented"/></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">Thrown if you pass <c>null</c> or an empty <see cref="String"/> as the <paramref name="jsonFileName"/> parameter.</exception>
    public static JsonPrefs FromFile
    (
        string jsonFileName,
        bool loadAfterConstruction = true,
        JsonSerializerOptions? jsonFormatting = null
    )
    {
        if (jsonFileName.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(FromFile)}: null or empty {nameof(jsonFileName)} string argument passed. Please only pass valid file names into this method!", nameof(jsonFileName));
        }

        if (!jsonFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            jsonFileName += ".json";
        }

        if (INSTANCES.ContainsKey(jsonFileName))
        {
            return INSTANCES[jsonFileName];
        }

        return INSTANCES[jsonFileName] = new JsonPrefs(jsonFileName, loadAfterConstruction, jsonFormatting);
    }

    private JsonPrefs
    (
        string jsonFileName,
        bool loadAfterConstruction = true,
        JsonSerializerOptions? jsonFormatting = null
    )
    {
        this.jsonFileName = jsonFileName;
        this.jsonFormatting = jsonFormatting;

        var version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);

        directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", version.CompanyName ?? "Glitched Polygons", version.ProductName ?? "Temp");

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        jsonFilePath = Path.Combine(directory, this.jsonFileName);

        if (loadAfterConstruction)
        {
            Load();
        }
    }

    /// <summary>
    /// Unloads all cached <see cref="JsonPrefs"/> instances.
    /// </summary>
    public static void UnloadAll()
    {
        INSTANCES.Clear();
    }

    /// <summary>
    /// Loads prefs from the underlying <c>.json</c> file on disk into the <see cref="JsonPrefs"/> instance. 
    /// </summary>
    /// <returns>Whether the loading procedure was successfully completed or not.</returns>
    public bool Load()
    {
        if (!File.Exists(jsonFilePath))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(jsonFilePath);

            IDictionary<string, string>? deserializedPrefs = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (deserializedPrefs is not null && deserializedPrefs.Count != 0)
            {
                prefs = deserializedPrefs;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes the <see cref="JsonPrefs"/> out to disk.
    /// </summary>
    public void Save()
    {
        File.WriteAllText(jsonFilePath, JsonSerializer.Serialize(prefs, jsonFormatting));
    }

    /// <summary>
    /// Writes the <see cref="JsonPrefs"/> out to disk asynchronously.
    /// </summary>
    /// <returns><see cref="Task"/></returns>
    public Task SaveAsync()
    {
        string json = JsonSerializer.Serialize(prefs, jsonFormatting);
        return File.WriteAllTextAsync(jsonFilePath, json);
    }

    /// <summary>
    /// Checks whether this <see cref="JsonPrefs"/> contains a given <paramref name="key"/> or not.
    /// </summary>
    /// <param name="key"><see cref="JsonPrefs"/> entry lookup key.</param>
    /// <returns>Whether the <see cref="JsonPrefs"/> instance contains an entry with the passed looku <paramref name="key"/>.</returns>
    public bool HasKey(string key)
    {
        return key.NotNullNotEmpty() && prefs.ContainsKey(key);
    }

    /// <summary>
    /// Deletes all the entries from the <see cref="JsonPrefs"/>.
    /// </summary>
    /// <remarks>
    /// Note that this will not automatically also <see cref="Save"/> the changes: you need to call <see cref="Save"/> or <see cref="SaveAsync"/> yourself manually after this if you want to persist your decision. 
    /// </remarks>
    public void DeleteAll()
    {
        prefs.Clear();
    }

    /// <summary>
    /// Removes an entry from the <see cref="JsonPrefs"/>.
    /// </summary>
    /// <param name="key">The lookup key of the prefs entry to remove.</param>
    /// <returns><c>true</c> if the entry was removed; <c>false</c> if nothing happened due to the passed <paramref name="key"/> not even being found in the prefs dictionary in the first place.</returns>
    /// <exception cref="ArgumentException">Thrown if you pass <c>null</c> or an empty <see cref="String"/>.</exception>
    /// <remarks>
    /// Note that this will not automatically also <see cref="Save"/> the changes: you need to call <see cref="Save"/> or <see cref="SaveAsync"/> yourself manually after this if you want to persist your decision. 
    /// </remarks>
    public bool DeleteKey(string key)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(DeleteKey)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        return prefs.Remove(key);
    }

    /// <summary>
    /// Returns the value corresponding to <paramref name="key"/> in the <see cref="JsonPrefs"/> file if it exists.
    /// If it doesn't exist, this method will return <paramref name="defaultValue"/>.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="defaultValue">The default value to return if <paramref name="key"/> isn't found in the prefs.</param>
    /// <returns>The found value, or <paramref name="defaultValue"/> if the key wasn't found inside the loaded prefs.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public float GetFloat(string key, float defaultValue = default)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(GetFloat)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        if (!prefs.TryGetValue(key, out string? stringValue))
        {
            return defaultValue;
        }

        if (!float.TryParse(stringValue, out float floatValue))
        {
            return defaultValue;
        }

        return floatValue;
    }

    /// <summary>
    /// Returns the value corresponding to <paramref name="key"/> in the <see cref="JsonPrefs"/> file if it exists.
    /// If it doesn't exist, this method will return <paramref name="defaultValue"/>.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="defaultValue">The default value to return if <paramref name="key"/> isn't found in the prefs.</param>
    /// <returns>The found value, or <paramref name="defaultValue"/> if the key wasn't found inside the loaded prefs.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public double GetDouble(string key, double defaultValue = default)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(GetFloat)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        if (!prefs.TryGetValue(key, out string? stringValue))
        {
            return defaultValue;
        }

        if (!double.TryParse(stringValue, out double doubleValue))
        {
            return defaultValue;
        }

        return doubleValue;
    }

    /// <summary>
    /// Returns the value corresponding to <paramref name="key"/> in the <see cref="JsonPrefs"/> file if it exists.
    /// If it doesn't exist, this method will return <paramref name="defaultValue"/>.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="defaultValue">The default value to return if <paramref name="key"/> isn't found in the prefs.</param>
    /// <returns>The found value, or <paramref name="defaultValue"/> if the key wasn't found inside the loaded prefs.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public int GetInt(string key, int defaultValue = default)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(GetInt)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        if (!prefs.TryGetValue(key, out string? stringValue))
        {
            return defaultValue;
        }

        if (!int.TryParse(stringValue, out int intValue))
        {
            return defaultValue;
        }

        return intValue;
    }

    /// <summary>
    /// Returns the value corresponding to <paramref name="key"/> in the <see cref="JsonPrefs"/> file if it exists.
    /// If it doesn't exist, this method will return <paramref name="defaultValue"/>.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="defaultValue">The default value to return if <paramref name="key"/> isn't found in the prefs.</param>
    /// <returns>The found value, or <paramref name="defaultValue"/> if the key wasn't found inside the loaded prefs.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public long GetLong(string key, long defaultValue = default)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(GetLong)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        if (!prefs.TryGetValue(key, out string? stringValue))
        {
            return defaultValue;
        }

        if (!long.TryParse(stringValue, out long longValue))
        {
            return defaultValue;
        }

        return longValue;
    }

    /// <summary>
    /// Returns the value corresponding to <paramref name="key"/> in the <see cref="JsonPrefs"/> file if it exists.
    /// If it doesn't exist, this method will return <paramref name="defaultValue"/>.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="defaultValue">The default value to return if <paramref name="key"/> isn't found in the prefs.</param>
    /// <returns>The found value, or <paramref name="defaultValue"/> if the key wasn't found inside the loaded prefs.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public bool GetBool(string key, bool defaultValue = default)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(GetBool)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        if (!prefs.TryGetValue(key, out string? stringValue))
        {
            return defaultValue;
        }

        if (!bool.TryParse(stringValue, out bool boolValue))
        {
            return defaultValue;
        }

        return boolValue;
    }

    /// Shorthand for calling <see cref="GetString"/>.
    public string this[string key] => GetString(key);

    /// <summary>
    /// Returns the value corresponding to <paramref name="key"/> in the <see cref="JsonPrefs"/> file if it exists.
    /// If it doesn't exist, this method will return <paramref name="defaultValue"/>.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="defaultValue">The default value to return if <paramref name="key"/> isn't found in the prefs.</param>
    /// <returns>The found value, or <paramref name="defaultValue"/> if the key wasn't found inside the loaded prefs.</returns>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public string GetString(string key, string defaultValue = "")
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(GetString)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        if (!prefs.TryGetValue(key, out string? stringValue))
        {
            return defaultValue;
        }

        return stringValue;
    }

    /// <summary>
    /// Sets the <c>float</c> value of the preference identified by the given <paramref name="key"/>.
    /// You can use <see cref="GetFloat"/> to retrieve this value.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="value">Value to write into the <see cref="JsonPrefs"/> under the given <paramref name="key"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public void SetFloat(string key, float value)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(SetFloat)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        prefs[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sets the <c>double</c> value of the preference identified by the given <paramref name="key"/>.
    /// You can use <see cref="GetDouble"/> to retrieve this value.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="value">Value to write into the <see cref="JsonPrefs"/> under the given <paramref name="key"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public void SetDouble(string key, double value)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(SetDouble)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        prefs[key] = value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Sets the <see cref="Int32"/> value of the preference identified by the given <paramref name="key"/>.
    /// You can use <see cref="GetInt"/> to retrieve this value.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="value">Value to write into the <see cref="JsonPrefs"/> under the given <paramref name="key"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public void SetInt(string key, int value)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(SetInt)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        prefs[key] = value.ToString();
    }

    /// <summary>
    /// Sets the <see cref="Int64"/> value of the preference identified by the given <paramref name="key"/>.
    /// You can use <see cref="GetLong"/> to retrieve this value.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="value">Value to write into the <see cref="JsonPrefs"/> under the given <paramref name="key"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public void SetLong(string key, long value)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(SetLong)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        prefs[key] = value.ToString();
    }

    /// <summary>
    /// Sets the <see cref="Boolean"/> value of the preference identified by the given <paramref name="key"/>.
    /// You can use <see cref="GetBool"/> to retrieve this value.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="value">Value to write into the <see cref="JsonPrefs"/> under the given <paramref name="key"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public void SetBool(string key, bool value)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(SetBool)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        prefs[key] = value.ToString();
    }

    /// <summary>
    /// Sets the <see cref="String"/> value of the preference identified by the given <paramref name="key"/>.
    /// You can use <see cref="GetString"/> to retrieve this value.
    /// </summary>
    /// <param name="key">Lookup key string of the desired pref.</param>
    /// <param name="value">Value to write into the <see cref="JsonPrefs"/> under the given <paramref name="key"/>.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="key"/> is <c>null</c> or empty.</exception>
    public void SetString(string key, string value)
    {
        if (key.NullOrEmpty())
        {
            throw new ArgumentException($"{nameof(JsonPrefs)}::{nameof(SetString)}: null or empty {nameof(key)} string passed. Please pass only valid arguments into this method! ", nameof(key));
        }

        prefs[key] = value;
    }
}