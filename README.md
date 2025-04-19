## Updated to 1.8.7.0

## What is this?
C# mod for Barotrauma that replaces Barotrauma code with identical Harmony patches

Designed to be used as template for other c# mods
## Why???
Because now i have 2 c# mods depending on similar set of methods, and i think it's easier to apply baro updates to this and then apply mod specific changes on top of that than to apply baro updates in two places

And i can imagine other modders (or future me) would want to skip monkey work and just copy paste this

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
