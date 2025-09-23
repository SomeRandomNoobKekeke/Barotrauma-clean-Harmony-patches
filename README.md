## Updated to 1.10.4.0

## What is this?
C# mod for Barotrauma that replaces Barotrauma code with identical Harmony patches

Designed to be used as template for other c# mods

If you need some method thats not here just ask me to convert it or make a pr, it's easy
## Why???
To reuse this code whenever i want to rewrite some old mod from scratch or start a new one that depends on these methods

Also i merge all barotrauma changes here so i wouldn't have to check the whole barotrauma diff for each mod to see if i need to update

## How???
Secret method:
- Copy paste original method, make it public static bool
- Copy paste using statements from original file
- Add to parameters __instance if original isn't static and "ref __result" if original isn't void
- Replace all "return x" with "__result = x; return false;"
- Add final "return false;"
- Add "OriginalClassType" _ = __instance; (or don't)
- cl_reloadlua and get a lot of errors
- Add _. to all missing var names
- Still missing names? Add OriginalClassType. to missing vars
- Improvise

Sometimes there are calls to base classes, and you might not have access to base classes, sometimes vars are passed by ref, some methods might get inlined, types defined in nested namespace might not see Barotrauma namespace for some reason, and so on

Also note that i was too lazy to test #if DEBUG stuff so it's broken
