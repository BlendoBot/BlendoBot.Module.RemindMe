# BlendoBot.Module.RemindMe
## Gives you friendly reminders of things at a later point in time.
![GitHub Workflow Status](https://img.shields.io/github/workflow/status/BlendoBot/BlendoBot.Module.RemindMe/Tests)

Want to give someone a nudge when it's their birthday? How about remembering when ticket sales open up? The RemindMe module gives you the ability to set yourself a reminder, and get pinged about it at a later point in time!

## Dependencies
This module relies on [BlendoBot.Module.UserTimeZone](https://github.com/BlendoBot/BlendoBot.Module.UserTimeZone) to interpret and print times in the user's local time.

## Discord Usage
- `?remind at [date/time] to [message]` - Sets a reminder for a specific time (adjusted based on the `UserTimeZone`).
- `?remind at [date/time] every (interval) to [message]` - Sets a reminder for a specific time, and then repeat every interval until the user deletes the message with `?remind list`.
- `?remind in [timespan] to [message]` - Sets a reminder to go after a given amount of time.
- `?remind in [timespan] every (interval) to [message]` - Sets a reminder to go after a given amount of time, and then repeat every interval until the user deletes the message with `?remind list`.
- `?remind list` - Shows all reminders the user has set, paginated 10 per page, and lets them selectively delete them with reactions.

All dates are output to the user in the format of `d/MM/yyyy h:mm:ss tt`.

### Admin commands
These commands are only available to BlendoBot admins.
- `?remind admin list` - Shows all reminders from everyone on the guild, and lets the user delete any of them with reactions.
- `?remind admin minrepeattime [num]` - Sets the minimum time a reminder can be repeated (in seconds). This defaults to 300 seconds. Setting this to 0 disables the repeat functionality.
- `?remind admin maxreminders [num]` - Sets the maximum number of reminders a user can set. Once they've set that many, they can't make any more unless they delete them.

### Date/time formats
The date/time is custom parsed in the following formats. Either the date or the time can be put first (e.g. `25/12/21 3:00` is equivalent to `3:00 25/12/21`).

#### Dates
- `dd/mm/yyyy` - (e.g. `25/12/2021`)
- `dd/mm/yy` - (e.g. `25/12/21`)
- `dd/mm` - The year is implied to be the current year (e.g. `25/12`)

#### Times
Times are always in 24 hour time!
- `hh:mm:ss` - (e.g. `13:40:00`)
- `hh:mm` - The seconds are implied to be `00` (e.g. `4:20`)

### Timespan formats
- `hh:mm:ss` - (e.g. `1:20:00`)
- `mm:ss` - Hours are implied to be `00` (e.g. `1:30`)

### Repeat formats
- `(second(s) | minute(s) | hour(s) | day(s))` - (e.g. `every hour`)
- `x (second(s) | minute(s) | hour(s) | day(s))` - (e.g. `every 4 days`)