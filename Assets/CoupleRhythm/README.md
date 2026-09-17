# Two Hearts, One Beat

This folder contains the runtime-built two-player rhythm game.

## Controls

- On launch, the KakaoPay QR is shown. The operator presses `입금 완료 · 노래 고르기` after confirming payment to open song selection.
- The confirmation button is a manual confirmation step; it does not query KakaoPay or a bank server.
- When a round ends, the final accuracy and judgement stats are shown. The operator presses `다음 게임하기` to return to the QR payment screen.
- `1P`: blue button
- `2P`: red button
- `Together!`: press the blue and red buttons together
- Development keyboard fallback: `A` for 1P and `L` for 2P
- Admin settings: use the gear button on the song-select or gameplay screen
- Return to song select during play: `Esc`

## Accuracy and prizes

Accuracy is normalized by the complete chart, so songs with different durations and note counts use the same prize condition.

- `PERFECT`: 100% credit
- `GOOD`: 60% credit
- `MISS`: 0% credit
- Duet and hold notes have double weight because they require two inputs or sustained input.
- Every wrong press adds the same penalty as one missed normal note.
- Prize condition: final accuracy of 80% or higher

## Adding authorised music

Put audio files that you are allowed to use in `Assets/Resources/CoupleRhythm/Audio/` with these exact file names (Unity-supported extension may vary):

- `redred`
- `its_me`
- `lemonade`
- `rude`

When a file is absent, the game automatically uses a generated demo beat so every song card remains playable. BPM values, hit windows, duet sync tolerance, audio offset, and heart density can be adjusted from the in-game admin panel.

Every round builds a fresh chart by combining random four-beat rhythm templates. Notes remain locked to quarter-, half-, or whole-beat positions, while the generator varies rests, alternating patterns, three-to-four-note rolls, duet notes, and one-to-two-beat hold notes. Higher admin note-density levels unlock denser subdivisions.

Density level 4 uses dedicated high-density templates with roughly 6–12 notes per four-beat measure, frequent quarter-beat streams, more alternating inputs, and a higher duet-note rate. Sparse level 1–2 templates are excluded at this level.

Duet notes use a higher 20.5–28% chance on eligible beats, depending on note density. The generator also prevents more than two consecutive measures from passing without a duet opportunity so the couple mechanic stays central to each round.

Hold notes must be pressed on the head and kept down until the tail reaches the target; releasing close to the end beat is also accepted. Pressing a button when no matching note is inside the judgement window costs 250 points, and wrong presses are listed separately on the result screen.

Starter BPM values are `121 / 147 / 128 / 128` in the same order as the list above. The bundled masters use measured per-song beat-grid offsets, while the admin audio offset remains available for device/input-latency calibration. If an audio file is replaced with a different master, its beat-grid offset must be measured again.

## Highlight play ranges

The full songs are not played. Each round starts ten seconds before the selected highlight and fades out at the end of that highlight:

- `REDRED`: 0:30–1:02 (highlight begins at 0:40)
- `it's me`: 0:22–0:59 (highlight begins at 0:32)
- `LEMONADE`: 0:59–1:29 (highlight begins at 1:09)
- `RUDE!`: 2:40–3:18 (highlight begins at 2:50)
