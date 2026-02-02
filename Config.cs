using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.IO;
using System.Linq;

namespace CircleToSearchCS
{
    static class Config
    {
        public const string HOTKEY = "PrintScreen"; // Ctrl+Alt+PrintScreen is not directly supported, will use PrintScreen
        public const string MODE = "CIRCLE"; // "BOX" or "CIRCLE"
        public static readonly Dictionary<string, string> SEARCH_MODES = new Dictionary<string, string>
        {
            { "AI Mode", "https://google.com" }
        };
        public const string BOX_COLOR = "#4287f4";
        public const string TEXT_COLOR = "#4287f4";
        public const double BROWSER_LOAD_WAIT_TIME = 1.0;
    }
}
