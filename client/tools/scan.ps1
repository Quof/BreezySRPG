# Static scan: a report per game of what the client can't read, values outside what it knows, missing files and
# a histogram of every feature the game uses (compare two games' reports to see what a new one exercises).
#   scan.ps1 -Out DIR GAMEDIR [GAMEDIR...]
param([Parameter(Mandatory)][string]$Out, [Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Games)
. "$PSScriptRoot\common.ps1"
& $scanner $Out @Games
