using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VMGenerator.Services
{
    public sealed class Patcher
    {
        private const double CLIP_TIMEOUT_SEC = 15.0;
        private static readonly string[] GEN_EXES = {
            "create_config_27122023.exe",
            "HEXWARE-Random-IntelMac.exe",
            "randomserialssd.exe",
        };

        private static readonly Regex RxArgsPort = new(@"0\.0\.0\.0:(\d{2})", RegexOptions.Compiled);
        private static readonly Regex RxMac = new(@"(e1000=)([^\s,]+)", RegexOptions.Compiled);
        private static readonly Regex RxBridge = new(@"bridge=vmbr(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxSerial = new(@"(serial=)([A-Za-z0-9\-]+)", RegexOptions.Compiled);

        public sealed class PatchResult
        {
            public string Patched { get; init; } = "";
            public List<Change> Changes { get; init; } = new();
            public string FirstArgsLine { get; init; } = "";
            public string GeneratedIp { get; init; } = "";
        }

        public async Task<PatchResult> BuildPatchedAsync(string cfg, string vmName)
        {
            // 1. Extract number from Name (WoW8 -> 8)
            int vmNumber = ExtractVmNumber(vmName);
            int vmbr = vmNumber; // VMBR ID = Number from name

            // 2. Calculate IP
            // Subnet = 110 + (VMBR_ID - 1)
            int subnet = 110 + (vmbr - 1);
            int host = Random.Shared.Next(10, 91); // Range [10, 90]
            string targetIp = $"192.168.{subnet}.{host}";
            
            // SMBIOS value to inject/replace
            // We want to achieve: type=11,value=192.168.x.x
            
            string argsBlock = await GrabFromEmbeddedAsync(GEN_EXES[0], CLIP_TIMEOUT_SEC);
            string newMac = await GrabFromEmbeddedAsync(GEN_EXES[1], CLIP_TIMEOUT_SEC);
            string newSn = await GrabFromEmbeddedAsync(GEN_EXES[2], CLIP_TIMEOUT_SEC);

            string eol = cfg.Contains("\r\n") ? "\r\n" : "\n";
            argsBlock = argsBlock.Replace("\r\n", "\n").Replace("\n", eol).TrimEnd('\r', '\n');

            int port = (Math.Abs(vmNumber) % 100) + 10;
            argsBlock = RxArgsPort.Replace(argsBlock, $"0.0.0.0:{port:00}");

            // 3. Inject/Update IP in argsBlock
            // Regex to match: type=11,value=... up to a comma or single quote
            var smbios11Regex = new Regex(@"(type=11,value=)([^,']*)");
            
            if (smbios11Regex.IsMatch(argsBlock))
            {
                // Replace existing value using ${1} to prevent $1 + 1... becoming $11
                argsBlock = smbios11Regex.Replace(argsBlock, $"${{1}}{targetIp}");
            }
            else
            {
                // If type=11 is missing entirely, we append it.
                // We assume argsBlock is a list of flags. We'll append a new -smbios flag.
                argsBlock += $" -smbios 'type=11,value={targetIp}'";
            }

            var lines = cfg.Replace("\r\n", "\n").Split('\n').ToList();

            string oldArgsLine = lines.FirstOrDefault(l => l.TrimStart().StartsWith("args:")) ?? "";
            string oldMac = ExtractFirst(lines, RxMac, 2);
            string oldVmbr = ExtractFirst(lines, RxBridge, 1);
            string oldSn = ExtractFirst(lines.Where(l => l.TrimStart().StartsWith("sata0:")), RxSerial, 2);
            string oldPort = ExtractPort(oldArgsLine);

            int iArgs = lines.FindIndex(l => l.TrimStart().StartsWith("args:"));
            if (iArgs >= 0) lines.RemoveAt(iArgs);
            
            // Insert new args
            int iBalloon = lines.FindIndex(l => l.TrimStart().StartsWith("balloon:"));
            if (iBalloon < 0) iBalloon = 0;
            lines.Insert(iBalloon, argsBlock);

            for (int i = 0; i < lines.Count; i++)
            {
                string l = lines[i];
                string lt = l.TrimStart();

                if (lt.StartsWith("net", StringComparison.OrdinalIgnoreCase))
                {
                    l = RxMac.Replace(l, m => $"{m.Groups[1].Value}{newMac}");
                    l = RxBridge.Replace(l, $"bridge=vmbr{vmbr}");
                    lines[i] = l;
                }
                else if (lt.StartsWith("sata0:", StringComparison.OrdinalIgnoreCase))
                {
                    l = RxSerial.Replace(l, m => $"{m.Groups[1].Value}{newSn}");
                    lines[i] = l;
                }
            }

            string result = string.Join(eol, lines);

            static string TrimArgs(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                s = s.Trim();
                if (s.StartsWith("args:", StringComparison.OrdinalIgnoreCase))
                    s = s.Substring(5).Trim();
                return s;
            }
            static string Abbrev(string s, int max = 600)
            {
                s = (s ?? "").Trim();
                if (s.Length <= max) return s;
                return s.Substring(0, max - 1) + "…";
            }

            var changes = new List<Change>
            {
                new Change("MAC (e1000)", oldMac, newMac),
                new Change("Serial (sata0)", oldSn, newSn),
                new Change("Bridge", string.IsNullOrEmpty(oldVmbr) ? "" : $"vmbr{oldVmbr}", $"vmbr{vmbr}"),
                new Change("VNC port", oldPort, $"{port:00}"),
                new Change("IP (SMBIOS)", "", targetIp),
                new Change("args", Abbrev(TrimArgs(oldArgsLine)), Abbrev(TrimArgs(argsBlock.Split(new[]{eol},StringSplitOptions.None)[0])))
            };

            return new PatchResult
            {
                Patched = result,
                Changes = changes,
                FirstArgsLine = argsBlock.Split(new[] { eol }, StringSplitOptions.None)[0],
                GeneratedIp = targetIp
            };
        }

        private static int ExtractVmNumber(string name)
        {
            var match = Regex.Match(name ?? "", @"(\d+)$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int n))
                return n;
            return 0; // Fallback
        }

        private static string ExtractFirst(IEnumerable<string> lines, Regex rx, int group)
        {
            foreach (var l in lines)
            {
                var m = rx.Match(l);
                if (m.Success) return m.Groups[group].Value;
            }
            return "";
        }

        private static string ExtractPort(string argsLine)
        {
            if (string.IsNullOrEmpty(argsLine)) return "";
            var m = RxArgsPort.Match(argsLine);
            return m.Success ? m.Groups[1].Value : "";
        }

        private static string SafeClipGet()
        {
            try
            {
                var dataPackage = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
                if (dataPackage.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
                {
                    var task = dataPackage.GetTextAsync().AsTask();
                    task.Wait();
                    return task.Result ?? string.Empty;
                }
                return string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string ExtractEmbeddedExe(string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            string? res = asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
            if (res == null) throw new FileNotFoundException($"Embedded resource not found: {fileName}");
            string tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}_{fileName}");
            using var src = asm.GetManifestResourceStream(res)!;
            using var dst = File.Create(tmp);
            src.CopyTo(dst);
            return tmp;
        }

        private static async Task<string> GrabFromEmbeddedAsync(string exeName, double timeoutSec)
        {
            string exePath = ExtractEmbeddedExe(exeName);
            string prevClip = SafeClipGet();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = new Process { StartInfo = psi };
                p.Start();

                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < timeoutSec)
                {
                    await Task.Delay(100);
                    string now = SafeClipGet();
                    if (!string.IsNullOrWhiteSpace(now) && now != prevClip)
                        return now.Trim();
                }
                if (!p.HasExited) await Task.WhenAny(Task.Run(() => p.WaitForExit(1000)), Task.Delay(1000));

                string stdout = (await p.StandardOutput.ReadToEndAsync()).Trim();
                if (!string.IsNullOrWhiteSpace(stdout)) return stdout;

                throw new InvalidOperationException($"{exeName} produced no data in {timeoutSec:0.#}s");
            }
            finally { try { File.Delete(exePath); } catch { } }
        }
    }
}