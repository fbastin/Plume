using System;

namespace Plume
{
    /// <summary>
    /// Reconnaît les terminaux (Windows Terminal, invite de commandes, PowerShell, WSL, PuTTY…).
    /// Leurs invites de mot de passe (sudo, ssh) ne se déclarent pas comme telles à Windows :
    /// par défaut, Plume n'y propose rien et n'y apprend rien.
    /// Les terminaux intégrés à un autre programme (celui de VS Code, par exemple) ne sont pas reconnus.
    /// </summary>
    internal static class Terminals
    {
        static readonly string[] WindowClasses =
        {
            "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PseudoConsoleWindow",
            "mintty", "VirtualConsoleClass", "PuTTY", "KiTTY"
        };

        // Noms de programme, sans « .exe ».
        static readonly string[] Programs =
        {
            "windowsterminal", "openconsole", "conhost", "cmd", "powershell", "pwsh", "wsl", "wslhost",
            "bash", "putty", "kitty", "mintty", "alacritty", "wezterm-gui", "mobaxterm", "conemu", "conemu64",
            "tabby", "hyper", "cmder"
        };

        public static bool Is(string windowClass, string processName)
        {
            foreach (string c in WindowClasses)
                if (string.Equals(c, windowClass, StringComparison.OrdinalIgnoreCase))
                    return true;
            foreach (string p in Programs)
                if (string.Equals(p, processName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
