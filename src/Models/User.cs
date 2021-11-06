using System;
using System.Collections.Generic;
using System.Text;

namespace Discord_Bot_Csharp.src.Models
{
    public class User
    {
        public ulong DiscordId { get; set; }
        public string GameName { get; set; }
        public string DiscordName { get; set; }
        public string Role { get; set; }
    }
}
