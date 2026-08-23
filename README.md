# Egghead

Egghead is a free, modern word puzzle game inspired by _Bookworm_. Build words by tracing through adjacent letter tiles, earn score multipliers from special tiles, and keep fire tiles from reaching the bottom of the board.

## Features

- Hex-like letter board with touch-and-drag word selection
- Weighted letter generation based on the included word list and current board
- Bonus, gold, diamond, and fire tile mechanics
- Score-based levels and increasing fire pressure
- Board shuffling and animated tile movement
- Local save data with optional Unity Authentication and Cloud Save
- Sound, music volume, frame-rate, safe-area, and tutorial controls

## Getting started

Egghead is built with **Unity 6.4** (`6000.4.6f1`).

1. Clone the repository.
2. Open the project through Unity Hub using the matching Unity version.
3. Open `Assets/Scenes/Title.unity` and enter Play mode.

The title and gameplay scenes are already included in Build Settings. Local saves work without a cloud connection; authentication and cloud saves require the project to be linked to a configured Unity Gaming Services project.

Egghead supports portrait and upside-down portrait orientations. The officially tested display range is from 3:4 through 9:21 (width:height); landscape orientations are not supported.

## Project structure

```text
Assets/
  GameData/   Letter and word data
  Prefabs/    Reusable game objects
  Scenes/     Title and main gameplay scenes
  Scripts/    Gameplay, UI, input, audio, and persistence code
  Sound/      Sound effects and music assets
  Sprites/    Game artwork
```

Most game logic lives in `Assets/Scripts/GameManager.cs`. Save coordination is handled by `SaveManager.cs`, while `LevelManager.cs`, `UIManager.cs`, and `LetterTile.cs` handle progression, presentation, and individual tile behavior.

## License

Egghead is available under the [MIT License](LICENSE). _Bookworm_ is referenced only as inspiration; this project is not affiliated with or endorsed by its original creators or publishers.
