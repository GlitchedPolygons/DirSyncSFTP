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

using System.Security.Cryptography;
using GlitchedPolygons.ExtensionMethods;

namespace DirSyncSFTP;

/// <summary>
/// Extension methods for <see cref="System.String"/>s.
/// </summary>
public static class StringExtensionMethods
{
    /// <summary>
    /// Wraps a verbose <see cref="ProtectedData"/>.<see cref="ProtectedData.Protect"/> call into a parameterless extension method invocation.
    /// </summary>
    /// <param name="str"><see cref="System.String"/> to encrypt.</param>
    /// <returns>Encrypted <see cref="System.String"/>.</returns>
    public static string Protect(this string str)
    {
        return ProtectedData
            .Protect(str.UTF8GetBytes(), null, DataProtectionScope.CurrentUser)
            .ToBase64String();
    }

    /// <summary>
    /// Wraps a verbose <see cref="ProtectedData"/>.<see cref="ProtectedData.Unprotect"/> call into a parameterless extension method invocation.
    /// </summary>
    /// <param name="str"><see cref="System.String"/> to decrypt.</param>
    /// <returns>Decrypted <see cref="System.String"/>.</returns>
    public static string Unprotect(this string str)
    {
        return ProtectedData
            .Unprotect(str.ToBytesFromBase64(), null, DataProtectionScope.CurrentUser)
            .UTF8GetString();
    }
}