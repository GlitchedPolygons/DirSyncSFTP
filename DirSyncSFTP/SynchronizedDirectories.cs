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

using System.Text.Json;
using System.Collections.Generic;
using System.Collections.Concurrent;
using GlitchedPolygons.ExtensionMethods;

namespace DirSyncSFTP;

public class SynchronizedDirectories
{
    private readonly JsonPrefs jsonPrefs;
    private readonly IDictionary<string, SynchronizedDirectory> synchronizedDirectories = new ConcurrentDictionary<string, SynchronizedDirectory>();

    public SynchronizedDirectories(JsonPrefs jsonPrefs)
    {
        this.jsonPrefs = jsonPrefs;
    }
    
    public IDictionary<string, SynchronizedDirectory> Dictionary => synchronizedDirectories;
    
    public void Load()
    {
        string encryptedJson = jsonPrefs.GetString(Constants.PrefKeys.SYNC_DIRECTORIES, string.Empty);

        if (encryptedJson.NullOrEmpty())
        {
            return;
        }
            
        string json = encryptedJson.Unprotect();

        IList<SynchronizedDirectory>? deserializedEntries = JsonSerializer.Deserialize<IList<SynchronizedDirectory>>(json);

        if (deserializedEntries is null)
        {
            return;
        }
            
        synchronizedDirectories.Clear();

        foreach (var synchronizedDirectory in deserializedEntries)
        {
            synchronizedDirectories[synchronizedDirectory.GetDictionaryKey()] = synchronizedDirectory;
        }
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(synchronizedDirectories);
            
        jsonPrefs.SetString(Constants.PrefKeys.SYNC_DIRECTORIES, json.Protect());
            
        jsonPrefs.Save();
    }
}