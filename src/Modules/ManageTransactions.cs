using Discord.Commands;
using Discord.WebSocket;
using Discord_Bot_Csharp.src.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;

namespace Discord_Bot_Csharp.src.Modules
{
    public class ManageTransactions : ModuleBase<SocketCommandContext>
    {
        string SettingsFilePath = Directory.GetCurrentDirectory() + "\\settings.json";
        string UsersFilePath = Directory.GetCurrentDirectory() + "\\users.json";
        string TransactionsFilePath = Directory.GetCurrentDirectory() + "\\transactions.json";
        Timer GeneralNotificationsTimer = new Timer();
        Timer PastDueNotificationsTimer = new Timer();

        [Command("verify")]
        [Summary("A human has verified the balance.")]
        public async Task Verify()
        {
            await ReplyAsync($"Balance confirmed! Thanks!");
        }

        [Command("update")]
        [Summary("A human has updated the balance including deposits not made on discord.")]
        public async Task update([Remainder] string newBalance = "")
        {
            var user = Context.User as SocketGuildUser;
            var currentUsers = await GetUsers();
            var userFound = false;
            foreach (var currentUser in currentUsers)
            {
                // This user is registered, get their role
                if (currentUser.DiscordId == user.Id && (currentUser.Role == "Governor" || currentUser.Role == "Consul" || currentUser.Role == "Officer"))
                {
                    var appSettings = await GetAppSettings();
                    if (double.TryParse(newBalance, out var balance))
                    {
                        appSettings.CurrentBalance = balance;
                        if (await SaveAppSettings(appSettings))
                            await ReplyAsync($"Balance updated! Thanks!");
                    }
                    else
                        await ReplyAsync($"Balance must be a number only!");
                    return;
                }
            }
            await ReplyAsync($"Balance can only be updated by a rank of officer or higher!");
        }

        [Command("deposit")]
        [Summary("Create a deposit transaction.")]
        public async Task Deposit([Remainder] string amountDeposited = "")
        {
            var user = Context.User as SocketGuildUser;
            

            if (amountDeposited.Length < 0)
            {
                await ReplyAsync($"Your amount to deposit must be specified! Try typing {Context.Client.CurrentUser.Mention} deposit 1000");
                return;
            }

            // load settings
            AppSettings AppSettings = await GetAppSettings();
            var ownerDepositRequirement = AppSettings.OwnerDepositRequirement;
            var consulDepositRequirement = AppSettings.ConsulDepositRequirement;
            var officerDepositRequirement = AppSettings.OfficerDepositRequirement;
            var settlerDepositRequirement = AppSettings.SettlerDepositRequirement;
            
            // Default to settler
            var roleName = "Settler"; 
            var requiredAmountToDeposit = settlerDepositRequirement;

            // Attempt to get registered user first
            var currentUsers = await GetUsers();
            var userFound = false;
            foreach (var currentUser in currentUsers)
            {
                // This user is registered, get their role
                if (currentUser.DiscordId == user.Id)
                {
                    roleName = currentUser.Role;
                    userFound = true;

                    // Get required deposit amount
                    if (roleName == "Governor")
                    {
                        requiredAmountToDeposit = ownerDepositRequirement;
                        roleName = "Governor";
                    }
                    else if (roleName == "Consul")
                    {
                        requiredAmountToDeposit = consulDepositRequirement;
                        roleName = "Consul";
                    }
                    else if (roleName == "Officer")
                    {
                        requiredAmountToDeposit = officerDepositRequirement;
                        roleName = "Officer";
                    }
                }
            }

            // User wasn't on the list yet
            // If no registered user found, try using their discord rank
            if (!userFound)
            {
                var ownerRole = (user as Discord.IGuildUser).Guild.Roles.FirstOrDefault(x => x.Name == "Owner");
                var consulRole = (user as Discord.IGuildUser).Guild.Roles.FirstOrDefault(x => x.Name == "Co-Owner");
                var officerRole = (user as Discord.IGuildUser).Guild.Roles.FirstOrDefault(x => x.Name == "New World Officer");
                var settlerRole = (user as Discord.IGuildUser).Guild.Roles.FirstOrDefault(x => x.Name == "Member");

                // Translate discord rank to New World in game ranks
                if (user.Roles.Contains(ownerRole))
                {
                    requiredAmountToDeposit = ownerDepositRequirement;
                    roleName = "Governor";
                }
                else if (user.Roles.Contains(consulRole))
                {
                    requiredAmountToDeposit = consulDepositRequirement;
                    roleName = "Consul";
                }
                else if (user.Roles.Contains(officerRole))
                {
                    requiredAmountToDeposit = officerDepositRequirement;
                    roleName = "Officer";
                }
            }

            if (double.TryParse(amountDeposited, out double amountToDeposit) && amountToDeposit > 0)
            {
                if (await AddDepositTransaction(user.Id, amountToDeposit, roleName, requiredAmountToDeposit))
                {
                    await ReplyAsync($"{user.Mention} has made a deposit of {amountToDeposit.ToString()}. Thanks for your contribution!");
                    AppSettings.CurrentBalance += await GetTransactionsTotal();
                    if (await SaveAppSettings(AppSettings))
                        await ReplyAsync("The new balance is: " + AppSettings.CurrentBalance + " Can someone please check the clan balance and confirm this is accurate");
                    else
                        await ReplyAsync("Error saving new balance");
                }
                else
                    await ReplyAsync($"Error making deposit! Please get an admin, as I need some human help.");
            }
            else
            {
                await ReplyAsync($"Your amount to deposit must be a whole number and greater than 0! Try typing {Context.Client.CurrentUser.Mention} deposit 1000");
                return;
            }
        }

        [Command("start")]
        [Summary("Start keeping track of all transactions.")]
        public async Task Start()
        {
            AppSettings AppSettings = await GetAppSettings();
            // Setup notifications
            GeneralNotificationsTimer.Enabled = true;
            GeneralNotificationsTimer.Interval = AppSettings.HoursBetweenGeneralNotifications * 3600000;
            GeneralNotificationsTimer.Elapsed += GeneralNotificationsTimer_Elapsed;
            GeneralNotificationsTimer.Start();

            PastDueNotificationsTimer.Enabled = true;
            PastDueNotificationsTimer.Interval = AppSettings.HoursBetweenPastDueNotifications * 3600000;
            PastDueNotificationsTimer.Elapsed += PastDueNotificationsTimer_Elapsed;
            PastDueNotificationsTimer.Start();

            await ReplyAsync("Now logging transactions");
            await ReplyAsync("Current Balance: " + AppSettings.CurrentBalance);
            await ReplyAsync("Last weekly reset date: " + GetLastWeeklyResetDate(AppSettings.WeeklyResetDay));
        }

        private async void PastDueNotificationsTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var transactions = await GetTransactions();
            var AppSettings = await GetAppSettings();
            var users = await GetUsers();

            // Go through all the registered users
            foreach (var user in users)
            {
                var lastWeeksUserPayments = new List<Transaction>();
                var currentWeeksUserPayments = new List<Transaction>();
                var mostRecentTransaction = new Transaction();

                // And all the transactions
                foreach (var transaction in transactions)
                {
                    // To find the transactions from this user
                    if (user.DiscordId == transaction.DiscordId)
                    {
                        // Get all payments made in last 7 days from last weeks reset day
                        if ((GetLastWeeklyResetDate(AppSettings.WeeklyResetDay) - transaction.TimeStamp).TotalDays <= 7)
                            lastWeeksUserPayments.Add(transaction);
                        // Get all payments made in last 7 days from the weekly reset day
                        if ((GetNextWeeklyResetDate(AppSettings.WeeklyResetDay) - transaction.TimeStamp).TotalDays <= 7)
                            currentWeeksUserPayments.Add(transaction);
                        if (mostRecentTransaction.TimeStamp == null || DateTime.Compare(mostRecentTransaction.TimeStamp, transaction.TimeStamp) < 0)
                            mostRecentTransaction = transaction;
                    }
                }

                // Add up all user's payments to see if they paid their required weekly amount
                double totalPayments = 0;
                foreach (var payment in currentWeeksUserPayments)
                    totalPayments += payment.DepositAmount;

                double totalLastWeekPayments = 0;
                foreach (var payment in lastWeeksUserPayments)
                    totalLastWeekPayments += payment.DepositAmount;

                // If user hasnt made any payments within last 7 days of last reset day then say they are past due, if less than that remind them to pay before reset day
                var message = "";
                var daysSinceLastWeeksPayment = (GetLastWeeklyResetDate(AppSettings.WeeklyResetDay) - mostRecentTransaction.TimeStamp).TotalDays;
                var daysSinceNextWeeksPayment = (GetNextWeeklyResetDate(AppSettings.WeeklyResetDay) - mostRecentTransaction.TimeStamp).TotalDays;
                if (mostRecentTransaction.TimeStamp == null || daysSinceLastWeeksPayment > 7 )
                {
                    message = "You are past due for last week ";
                    if (user.Role == "Governor" && totalPayments < AppSettings.OwnerDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.OwnerDepositRequirement - totalLastWeekPayments) + " asap to get current");
                    if (user.Role == "Consul" && totalPayments < AppSettings.ConsulDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.ConsulDepositRequirement - totalLastWeekPayments) + " asap to get current");
                    if (user.Role == "Officer" && totalPayments < AppSettings.OfficerDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.OfficerDepositRequirement - totalLastWeekPayments) + " asap to get current");
                    if (user.Role == "Settler" && totalPayments < AppSettings.SettlerDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.SettlerDepositRequirement - totalLastWeekPayments) + " asap to get current");
                }
                if (mostRecentTransaction.TimeStamp == null || daysSinceNextWeeksPayment > 8)
                {
                    message = "Friendly reminder: ";
                    if (user.Role == "Governor" && totalPayments < AppSettings.OwnerDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.OwnerDepositRequirement - totalPayments) + " asap to stay current this week");
                    if (user.Role == "Consul" && totalPayments < AppSettings.ConsulDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.ConsulDepositRequirement - totalPayments) + " asap to stay current this week");
                    if (user.Role == "Officer" && totalPayments < AppSettings.OfficerDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.OfficerDepositRequirement - totalPayments) + " asap to stay current this week");
                    if (user.Role == "Settler" && totalPayments < AppSettings.SettlerDepositRequirement)
                        await ReplyAsync(message + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.SettlerDepositRequirement - totalPayments) + " asap to stay current this week");
                }
            }
        }
        private async void GeneralNotificationsTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            var transactions = await GetTransactions();
            var AppSettings = await GetAppSettings();
            var users = await GetUsers();

            // Go through all the registered users
            foreach (var user in users)
            {
                var userPayments = new List<Transaction>();

                // And all the transactions
                foreach (var transaction in transactions)
                    // To find the transactions from this user
                    if (user.DiscordId == transaction.DiscordId)
                        // Get all payments made in last 7 days from the weekly reset day
                        if ((GetNextWeeklyResetDate(AppSettings.WeeklyResetDay) - transaction.TimeStamp).TotalDays <= 7)
                            userPayments.Add(transaction);

                // Add up all user's payments to see if they paid their required weekly amount
                double totalPayments = 0;
                foreach (var payment in userPayments)
                    totalPayments += payment.DepositAmount;

                if (user.Role == "Governor" && totalPayments < AppSettings.OwnerDepositRequirement)
                    await ReplyAsync("Friendly Reminder: " + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.OwnerDepositRequirement - totalPayments) + " asap to stay current this week");
                if (user.Role == "Consul" && totalPayments < AppSettings.ConsulDepositRequirement)
                    await ReplyAsync("Friendly Reminder: " + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.ConsulDepositRequirement - totalPayments) + " asap to stay current this week");
                if (user.Role == "Officer" && totalPayments < AppSettings.OfficerDepositRequirement)
                    await ReplyAsync("Friendly Reminder: " + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.OfficerDepositRequirement - totalPayments) + " asap to stay current this week");
                if (user.Role == "Settler" && totalPayments < AppSettings.SettlerDepositRequirement)
                    await ReplyAsync("Friendly Reminder: " + user.DiscordName + " aka " + user.GameName + " please pay " + (AppSettings.SettlerDepositRequirement - totalPayments) + " asap to stay current this week");

            }
        }
        private async Task<double> GetTransactionsTotal()
        {
            var transactions = await GetTransactions();
            double total = 0;
            foreach (Transaction transaction in transactions)
                total += transaction.DepositAmount;
            return total;         
        }
        private async Task<AppSettings> GetAppSettings()
        {
            AppSettings AppSettings;

            int x = 0;
            do
            {
                if (File.Exists(SettingsFilePath) && !IsFileLocked(new FileInfo(SettingsFilePath)))
                {
                    // Open the settings file to load settings
                    using FileStream stream = File.OpenRead(SettingsFilePath);
                    AppSettings = await JsonSerializer.DeserializeAsync<AppSettings>(stream);
                    stream.Close();
                    return AppSettings;
                }
                x++;
                await Task.Delay(1000);
            } while (x < 10);

            var message = "";
            if (!File.Exists(SettingsFilePath))
                message = "Settings file not found! Please see the read me file and then relaunch the app!";
            else
                message = "Settings file in-use, please try again.";
            await ReplyAsync(message);
            Console.WriteLine(message);
            return new AppSettings();
        }
        private async Task<bool> SaveAppSettings(AppSettings  appSettings)
        {
            int x = 0;
            do
            {
                if (File.Exists(SettingsFilePath) && !IsFileLocked(new FileInfo(SettingsFilePath)))
                {
                    // Open the file 
                    // Save the new settings to file
                    using FileStream createStream = File.Create(SettingsFilePath);
                    await JsonSerializer.SerializeAsync(createStream, appSettings, new JsonSerializerOptions() { WriteIndented = true });
                    createStream.Close();
                    return true;
                }
                x++;
                await Task.Delay(1000);
            } while (x < 10);

            var message = "";
            if (!File.Exists(SettingsFilePath))
                message = "Settings file not found! Please see the read me file and then relaunch the app!";
            else
                message = "Settings file in-use, please try again.";
            await ReplyAsync(message);
            Console.WriteLine(message);
            return false;
        }
        private async Task<List<Transaction>> GetTransactions()
        {
            var transactions = new List<Transaction>();
            int x = 0;
            do
            {
                // Get a list of all the transactions
                if (File.Exists(TransactionsFilePath) && !IsFileLocked(new FileInfo(TransactionsFilePath)))
                {
                    using FileStream fs = File.OpenRead(TransactionsFilePath);
                    transactions = await JsonSerializer.DeserializeAsync<List<Transaction>>(fs);
                    fs.Close();
                    return transactions;
                }
                x++;
                await Task.Delay(1000);
            } while (x < 10);

            var message = "";
            if (!File.Exists(TransactionsFilePath))
                message = "Transaction file not found! Please see the read me file and then relaunch the app!";
            else
                message = "Transaction file in-use, please try again.";
            await ReplyAsync(message);
            Console.WriteLine(message);
            return new List<Transaction>();
        }
        private async Task<List<User>> GetUsers()
        {
            var users = new List<User>();
            int x = 0;
            do
            {
                // Get a list of all the transactions
                if (File.Exists(UsersFilePath) && !IsFileLocked(new FileInfo(UsersFilePath)))
                {
                    using FileStream stream = File.OpenRead(UsersFilePath);
                    users = await JsonSerializer.DeserializeAsync<List<User>>(stream);
                    stream.Close();
                    return users;
                }
                x++;
                await Task.Delay(1000);
            } while (x < 10);

            var message = "";
            if (!File.Exists(UsersFilePath))
                message = "Users file not found! Please see the read me file and then relaunch the app!";
            else
                message = "Users file in-use, please try again.";
            await ReplyAsync(message);
            Console.WriteLine(message);
            return new List<User>();
        }
        public async Task<bool> AddDepositTransaction(ulong discordUserId, double amountToDeposit, string role, double requiredDepositAmount)
        {
            Transaction depositTransaction = new Transaction();
            depositTransaction.DiscordId = discordUserId;
            depositTransaction.DepositAmount = amountToDeposit;
            depositTransaction.RoleAtTimeOfDeposit = role;
            depositTransaction.RequiredDepositAmountAtTimeOfDeposit = requiredDepositAmount;
            depositTransaction.TimeStamp = DateTime.Now;

            // Check if there is a transaction file
            if (File.Exists(TransactionsFilePath) && !IsFileLocked(new FileInfo(TransactionsFilePath)))
            {
                // Open the file 
                using FileStream stream = File.OpenRead(TransactionsFilePath);
                var currentTransactions = await JsonSerializer.DeserializeAsync<List<Transaction>>(stream);
                stream.Close();
                
                // Add the new transaction
                currentTransactions.Add(depositTransaction);

                // Save the new transaction to file
                using FileStream createStream = File.Create(TransactionsFilePath);
                await JsonSerializer.SerializeAsync(createStream, currentTransactions, new JsonSerializerOptions() { WriteIndented = true });
                createStream.Close();

                return true;
            }
            else
            {
                // Create a new transaction file, add the first transaction, and save the file
                var newTransactionList = new List<Transaction>();
                newTransactionList.Add(depositTransaction);
                using FileStream createStream = File.Create(TransactionsFilePath);
                await JsonSerializer.SerializeAsync(createStream, newTransactionList, new JsonSerializerOptions() { WriteIndented = true });
                createStream.Close();

                return true;
            }
        }
        protected virtual bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
        }
        private DateTime GetLastWeeklyResetDate(string resetDay)
        {
            DateTime lastResetDay = DateTime.Now.AddDays(-1);

            switch (resetDay)
            {
                case "Monday":
                    while (lastResetDay.DayOfWeek != DayOfWeek.Monday)
                        lastResetDay = lastResetDay.AddDays(-1);
                    break;
                case "Tuesday":
                    while (lastResetDay.DayOfWeek != DayOfWeek.Tuesday)
                        lastResetDay = lastResetDay.AddDays(-1);
                    break;
                case "Wednesday":
                    while (lastResetDay.DayOfWeek != DayOfWeek.Wednesday)
                        lastResetDay = lastResetDay.AddDays(-1);
                    break;
                case "Thursday":
                    while (lastResetDay.DayOfWeek != DayOfWeek.Thursday)
                        lastResetDay = lastResetDay.AddDays(-1);
                    break;
                case "Friday":
                    while (lastResetDay.DayOfWeek != DayOfWeek.Friday)
                        lastResetDay = lastResetDay.AddDays(-1);
                    break;
                case "Saturday":
                    while (lastResetDay.DayOfWeek != DayOfWeek.Saturday)
                        lastResetDay = lastResetDay.AddDays(-1);
                    break;
                case "Sunday":
                    while (lastResetDay.DayOfWeek != DayOfWeek.Sunday)
                        lastResetDay = lastResetDay.AddDays(-1);
                    break;
            }

            return lastResetDay;
        }
        private DateTime GetNextWeeklyResetDate(string resetDay)
        {
            DateTime nextResetDay = DateTime.Now.AddDays(+1);

            switch (resetDay)
            {
                case "Monday":
                    while (nextResetDay.DayOfWeek != DayOfWeek.Monday)
                        nextResetDay = nextResetDay.AddDays(+1);
                    break;
                case "Tuesday":
                    while (nextResetDay.DayOfWeek != DayOfWeek.Tuesday)
                        nextResetDay = nextResetDay.AddDays(+1);
                    break;
                case "Wednesday":
                    while (nextResetDay.DayOfWeek != DayOfWeek.Wednesday)
                        nextResetDay = nextResetDay.AddDays(+1);
                    break;
                case "Thursday":
                    while (nextResetDay.DayOfWeek != DayOfWeek.Thursday)
                        nextResetDay = nextResetDay.AddDays(+1);
                    break;
                case "Friday":
                    while (nextResetDay.DayOfWeek != DayOfWeek.Friday)
                        nextResetDay = nextResetDay.AddDays(+1);
                    break;
                case "Saturday":
                    while (nextResetDay.DayOfWeek != DayOfWeek.Saturday)
                        nextResetDay = nextResetDay.AddDays(+1);
                    break;
                case "Sunday":
                    while (nextResetDay.DayOfWeek != DayOfWeek.Sunday)
                        nextResetDay = nextResetDay.AddDays(+1);
                    break;
            }

            return nextResetDay;
        }
        internal class AppSettings
        {
            public double HoursBetweenGeneralNotifications { get; set; }
            public double HoursBetweenPastDueNotifications { get; set; }
            public string WeeklyResetDay { get; set; }
            public double OwnerDepositRequirement { get; set; }
            public double ConsulDepositRequirement { get; set; }
            public double OfficerDepositRequirement { get; set; }
            public double SettlerDepositRequirement { get; set; }
            public double CurrentBalance { get; set; }
        }
    }
}
