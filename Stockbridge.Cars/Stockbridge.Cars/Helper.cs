using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Stockbridge.Cars
{
    internal static class Helper
    {
        public static string ClearText(this string text)
        {
            return text.Replace("\n", "").TrimStart().TrimEnd();
        }
    }
}
