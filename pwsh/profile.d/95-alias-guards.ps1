# Alias guards — must load after every other profile.d file.
#
# PowerShell resolves a command name in this order:
#
#     Alias  >  Function  >  Cmdlet  >  Application
#
# An alias therefore BEATS a function of the same name. Any built-in alias
# that collides with one of our functions silently wins, and the function
# looks broken for no visible reason — `gsv` running Get-Service instead of
# the git-show wrapper, `gl` running Get-Location instead of the pretty log.
#
# Individual files guard their own names with Remove-Item Alias:<name>, but
# that has to be remembered for every new function, and forgetting it produces
# a confusing bug rather than an error. This sweep is the safety net: after
# everything has loaded, any alias whose name matches a function we defined is
# removed, so the function wins. New functions are covered automatically.
foreach ($alias in @(Get-ChildItem Alias: -ErrorAction SilentlyContinue)) {
    if (Test-Path "Function:\$($alias.Name)") {
        try {
            Remove-Item "Alias:$($alias.Name)" -Force -ErrorAction Stop
        } catch {
            # Constant or read-only aliases cannot be removed — leave them be.
            # Nothing we define collides with those, so this is not a problem.
        }
    }
}
