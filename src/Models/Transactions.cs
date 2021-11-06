using System;
using System.Collections.Generic;
using System.Text;

namespace Discord_Bot_Csharp.src.Models
{
    public class Transaction
    {
        public ulong DiscordId { get; set; }
        public double DepositAmount { get; set; }
        public DateTime TimeStamp { get; set; }
        public string RoleAtTimeOfDeposit { get; set; }
        public double RequiredDepositAmountAtTimeOfDeposit { get; set; }
    }
}
