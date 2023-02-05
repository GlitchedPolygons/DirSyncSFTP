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

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DirSyncSFTP;

public class KnownHosts
{
    private readonly string knownHostsFile;
    private readonly IDictionary<string, string> knownHosts = new ConcurrentDictionary<string, string>();
    private static readonly JsonSerializerOptions JSON_SERIALIZER_OPTIONS = new() { WriteIndented = true };

    public KnownHosts(string knownHostsFile)
    {
        this.knownHostsFile = knownHostsFile;
    }

    public IDictionary<string, string> Dictionary => knownHosts;

    public void Load()
    {
        if (!File.Exists(knownHostsFile))
        {
            File.WriteAllText(knownHostsFile, "{}");
            return;
        }

        try
        {
            IDictionary<string, string>? deserializedKnownHosts = JsonSerializer.Deserialize<IDictionary<string, string>>(File.ReadAllText(knownHostsFile));

            knownHosts.Clear();

            foreach (KeyValuePair<string, string> kvp in deserializedKnownHosts!)
            {
                knownHosts.Add(kvp.Key, kvp.Value);
            }
        }
        catch
        {
            File.WriteAllText(knownHostsFile, "{}");
        }
    }

    public void Save()
    {
        File.WriteAllText(knownHostsFile, JsonSerializer.Serialize(knownHosts, JSON_SERIALIZER_OPTIONS));
    }
}