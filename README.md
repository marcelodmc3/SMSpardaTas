# SMSpardaTas

Compiles a Super Metroid TAS script (`.tas`) into a Lua script that plays it back frame by frame in
**BizHawk**. BizHawk handles the input injection, so playback is frame-exact and repeatable from a
savestate.

## Build and run

Needs the .NET 10 SDK.

```bash
dotnet build
dotnet test
```

```bash
dotnet run --project src/SMSpardaTas -- compile samples/landing_site.tas
```

The executable is `src/SMSpardaTas/bin/Debug/net10.0/SMSpardaTas.exe`:

| Command | What it does |
|---|---|
| `SMSpardaTas compile <script.tas> [-o out.lua] [--no-hud]` | Writes `<script>.bizhawk.lua` next to the script |
| `SMSpardaTas check <script.tas>` | Parses the script and prints a summary without writing anything |
| `SMSpardaTas symbols` | Lists the built-in RAM variables |

Errors look like `script.tas(12,7): error: unknown button 'Q' ...`.

## Playing it back

In BizHawk (SNES core, Snes9x or BSNES): *Tools > Lua Console*, then *Script > Open Script* and pick
the `.lua`.

The script loads the savestate, plays every frame, and pauses the emulator when it finishes or when a
check fails. The reason appears in the Lua console. A small HUD shows the script line, playback frame
and held buttons (turn it off with `--no-hud`).

## Script language

One statement per line. `#` or `;` starts a comment.

```text
@savestate slot 1                  ; BizHawk slots 1-9, or a file: @savestate "states/start.State"
# @controller { ... }               ; optional block: your controller layout (see "Controller layout")
@var door_timer $0925 u16          ; name your own RAM variables (u8, u16, s8, s16; default u16)

Right+B 60                         ; hold buttons for 60 frames
Right+B+A                          ; frame count defaults to 1
. 10                               ; no buttons for 10 frames (also: "none 10" or "wait 10")

repeat 3 {                         ; repeat blocks can nest
    Y 2
    . 2
}

wait_until game_state == 8 hold Left+B timeout 600
assert room == $91F8 "should be at the Landing Site"
label "Parlor"                     ; shown on the HUD and logged with its frame number
```

**Buttons:** `Up Down Left Right Select Start Y B X A L R` (case-insensitive), joined with `+`.

### Controller layout

By default, scripts are written with the game's default controller layout. If you play with a
different one, describe it in an `@controller` block before any input. Every input and `hold` in
the script is then written in *your* layout, and the compiler translates it to the game's default
buttons:

```text
@controller {
    shot        = Y          ; default X
    jump        = A          ; default A
    dash        = X          ; default B
    item_select = Select     ; default Select
    item_cancel = B          ; default Y
    angle_up    = R          ; default R
    angle_down  = L          ; default L
}

Left+X 30                    ; dash left   -> plays Left+B
Left+X+A+Y 4                 ; dash, jump and shoot -> plays Left+B+A+X
```

- Actions you leave out keep their default button. Each of `A B X Y Select L R` must end up on exactly
  one action. The d-pad and Start can't be remapped.
- The HUD and the generated Lua comments show buttons the way you wrote them.
- The game in your savestate must be on its **default** controller settings. With an `@controller`
  block, the script checks the game's bindings before the first frame. If they aren't the default,
  it stops with the values it found (`shot_binding`, `jump_binding`, ... are also usable in conditions).
- Conditions on `input` / `new_input` read the game's raw button bits, so they aren't translated.

**Frames** are emulator frames with lag frames included, the same as in a movie file. If Samus
lags for 3 frames during a door, a `Right 60` still lasts 60 emulator frames.

**`wait_until <condition> [hold <buttons>] [timeout N]`** checks the condition before each frame.
While it's false, the buttons in `hold` are held (none by default). If it's already true, it takes no
frames. `timeout` defaults to 3600 frames, and `timeout 0` waits forever. A timeout stops playback.

**`assert <condition> ["message"]`** checks immediately, without using a frame. If it fails, playback
stops and the log shows the values of every variable in the condition, e.g.
`line 9: start from the Landing Site: room == $91F8; room=37368 (0x91F8)`.

**Conditions** compare numbers and RAM variables with `== != < <= > >=`. You can combine them with
`and`, `or`, `not` and parentheses. `&` masks bits and binds tighter than comparisons, as in
`input & $80 == $80`.

- Numbers: `60`, `0x91F8`, `$91F8` and negative values like `-1` (for `s8`/`s16` variables).
- Variables: built-in names (`SMSpardaTas symbols`), your own `@var` declarations, or raw reads like
  `u16[$0AF6]` / `u8[$7F:1234]`.
- Addresses without a bank are taken as `$7E`.

Built-in variables include `room`, `game_state`, `area`, `samus_x`, `samus_y`, `health`,
`reserve`, `missiles`, `pose`, `boss_hp`, `mb2_hp`, `input`, `nmi_counter`, `lag_counter`, `rng` and
the in-game time. All come from `RAM map.txt`.

## Notes and limits

- Start every script from a savestate (`@savestate`). Without one, playback depends on which frame
  you happened to load the script on.
- `@savestate slot N` loads one of BizHawk's numbered slots, 1-9. `@savestate "file.State"` loads a
  savestate file; a relative path is resolved against the script's folder at compile time.
- Pressing Left+Right or Up+Down together is allowed by the script. In BizHawk this also needs
  *Config > Controllers > "Allow U+D/L+R"*.
- Repeat blocks are expanded when compiling. A script can expand to at most 250,000 steps.
- The tests run the generated Lua against a fake BizHawk API under MoonSharp, so they check playback
  logic, not BizHawk itself.
