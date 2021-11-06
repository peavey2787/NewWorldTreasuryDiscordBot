# New World Treasury Discord Bot in C# 

The bot needs to be told to start anytime the application is closed and reopened. Just type @NewWorldTreasury start in your treasury-channel.
The bot doesn't need to be told to start to accept donations or for registrations, only to check general/past due notifications.
It is very important that the application is running when it sends out its notifications at the time set in settings.
Each week will reset the amount each clan member has to pay.
If you deposit more than you have to it doesn't carry over to future weeks, it is considered a donation.

# Settings:
There should already be a settings.json file in the same folder as the .exe, but if not create one with this text:
{
  "HoursBetweenGeneralNotifications": 24,
  "HoursBetweenPastDueNotifications": 6,
  "WeeklyResetDay": "Sunday",
  "OwnerDepositRequirement": 1000,
  "ConsulDepositRequirement": 1000,
  "OfficerDepositRequirement": 750,
  "SettlerDepositRequirement": 100,
  "CurrentBalance": 34460
}

# Commands:
register @UserToRegister UsersInGameName/UsersInGameRank - Anyone can register anyone, must seperate name and role with /
remove @UserToRemove - Only the owner is able to remove users
deposit AmountToDeposit - Anyone can deposit, even if they haven't registered but it must only be a number; no commas or extras $
verify - Confirm the new balance
update - Must be registered and rank of officer or higher to use. Update balance due to donations in game not reported to discord
