using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using Discord_Bot_Csharp.src.Models;
using System.IO;

namespace Discord_Bot_Csharp.src.Modules
{
    public class ManageUsers : ModuleBase<SocketCommandContext>
    {
        string FilePath = Directory.GetCurrentDirectory() + "\\users.json";

        [Command("register")]
        [Summary("Register a new user.")]
        public async Task RegisterUser([Remainder] string gameNameAndRole = "")
        {
            var user = Context.User as SocketGuildUser;
            var role = "Settler";
            var gameName = gameNameAndRole; 

            try
            { 
                var input = gameNameAndRole.Split('/');
                gameName = input[0];
                role = input[1];
            }
            catch { }
            

            if (gameName.Length < 0)
            {
                await ReplyAsync($"Your in-game name must be specified! Try typing {Context.Client.CurrentUser.Mention} register YOURNEWWORLDGAMENAMEHERE");
                return;
            }

            if (await AddUser(user.Id, gameName, user.Mention, role))
                await ReplyAsync($"{user.Mention} is now registered as {gameName} with the rank of {role}");
            else
                await ReplyAsync($"User: {user.Id} / {gameName} / {user.Mention} is already registered!");
        }

        [Command("remove")]
        [Summary("Remove an existing user.")]
        public async Task RemoveUser(SocketGuildUser userToRemove)
        {
            var userSendingCommand = Context.User as SocketGuildUser;
            // Open the user file and see if this user is the owner
            using FileStream stream = File.OpenRead(FilePath);
            var users = await JsonSerializer.DeserializeAsync<List<User>>(stream);
            stream.Close();

            foreach (var user in users)
            {
                // The user requesting to remove another user is the owner
                if (userSendingCommand.Id == user.DiscordId && user.Role == "Governor")
                {
                    // Remove user
                    if (await RemoveUser(userToRemove.Id))
                        await ReplyAsync($"{userToRemove.Mention} has been removed!");
                    else
                        await ReplyAsync($"{userToRemove.Mention} was not removed! Please try again or get an admin, as I need some human help.");
                    return;
                }
            }
            await ReplyAsync($"{userToRemove.Mention} was not removed! {userSendingCommand.Mention} is not the owner and does not have this privilige.");
        }




        private async Task<bool> AddUser(ulong discordUserId, string gameName, string discordName, string role)
        {
            User user = new User();
            user.DiscordId = discordUserId;
            user.GameName = gameName;
            user.DiscordName = discordName;
            user.Role = role;

            // Check if there is a user file
            if ( File.Exists(FilePath) && !IsFileLocked(new FileInfo(FilePath)) )
            {
                // Open the file and see if this user is already on the list
                using FileStream stream = File.OpenRead(FilePath);
                var currentUsers = await JsonSerializer.DeserializeAsync<List<User>>(stream);
                stream.Close();
                
                foreach(var currentUser in currentUsers)
                {
                    // Don't add this user if they are already on the list
                    if (currentUser.DiscordId == user.DiscordId)
                        return false;
                }

                // User wasn't on the list yet, so add them and save the new list
                currentUsers.Add(user);
                using FileStream createStream = File.Create(FilePath);
                await JsonSerializer.SerializeAsync(createStream, currentUsers, new JsonSerializerOptions() { WriteIndented = true });
                createStream.Close();

                return true;
            }
            else
            {
                // Create a new list, add the user, and save the file
                var newUserList = new List<User>();
                newUserList.Add(user);
                using FileStream createStream = File.Create(FilePath);
                await JsonSerializer.SerializeAsync(createStream, newUserList, new JsonSerializerOptions() { WriteIndented = true });
                createStream.Close();

                return true;
            }
        }

        private async Task<bool> RemoveUser(ulong discordUserId)
        {
            // Check if there is a user file
            if ( File.Exists(FilePath) && !IsFileLocked(new FileInfo(FilePath)) )
            {
                // Open the file and find this user
                using FileStream stream = File.OpenRead(FilePath);
                var currentUsers = await JsonSerializer.DeserializeAsync<List<User>>(stream);
                stream.Close();

                var userToRemove = new User();

                foreach (var currentUser in currentUsers)
                {
                    // This is the user we are looking for
                    if (currentUser.DiscordId == discordUserId)
                        userToRemove = currentUser;
                }

                // User was removed from the list, so save the new list
                currentUsers.Remove(userToRemove);
                using FileStream createStream = File.Create(FilePath);
                await JsonSerializer.SerializeAsync(createStream, currentUsers, new JsonSerializerOptions() { WriteIndented = true });
                createStream.Close();

                return true;
            }
            else
            {
                return false;
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
    }
}
