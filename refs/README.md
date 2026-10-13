# Local BONEWORKS MelonLoader refs

Copy your game MelonLoader tree here:

`C:\Program Files (x86)\Steam\steamapps\common\BONEWORKS\BONEWORKS\MelonLoader`

Expected layout:

```
refs/MelonLoader/
  MelonLoader.dll
  0Harmony.dll
  Managed/
    Assembly-CSharp.dll
    UnhollowerBaseLib.dll
    UnhollowerRuntimeLib.dll
    UnityEngine*.dll
    ...
```

Override path: `msbuild /p:BoneworksMelonLoader=C:\...\MelonLoader`
