# UTF-8 console -- loads before anything else in profile.d prints.
#
# A Windows console starts on the OEM code page (850 on en-GB). Programs that
# write UTF-8 bytes to it (Claude Code, git, node tools) then come out as
# mojibake: every bullet, box-drawing line or arrow turns into three junk
# characters (a bullet shows up as "O-circumflex, u-grave, A-ring"). Each of
# those symbols now takes three columns instead of one, so a full-screen app
# loses track of where its lines end, and its redraws leave fragments of
# earlier output smeared across the bottom of the window.
#
# The code page belongs to the console, not to this process, so setting it
# here covers everything launched from this tab. It also means anything that
# saves and restores the code page around its own run (gws's Terminal does)
# now puts back UTF-8 instead of 850.
$script:Utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding  = $script:Utf8NoBom
[Console]::OutputEncoding = $script:Utf8NoBom
# What pwsh uses when piping text INTO native commands.
$global:OutputEncoding    = $script:Utf8NoBom
