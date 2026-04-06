//
// Copyright (c) Fela Ameghino 2015-2026
//
// Distributed under the GNU General Public License v3.0. (See accompanying
// file LICENSE or copy at https://www.gnu.org/licenses/gpl-3.0.txt)
//

using System;
using System.IO;
using Windows.Storage;

namespace Telegram.Common
{
    internal static class StartupTrace
    {
        private static readonly object _gate = new();

        public static void Write(string message)
        {
            try
            {
                var line = $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}";
                var path = GetPath();

                lock (_gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, line);
                }
            }
            catch
            {
                // Startup diagnostics should never become a new crash source.
            }
        }

        public static void Write(string stage, Exception ex)
        {
            Write($"{stage}: {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex}");
        }

        private static string GetPath()
        {
            try
            {
                return Path.Combine(ApplicationData.Current.LocalFolder.Path, "startup.log");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "Unigram.startup.log");
            }
        }
    }
}
