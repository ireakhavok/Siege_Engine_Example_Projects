# Chess (CastleBuilder project scripts)

Gray on **Load Project** is expected. Chess runs on **Play**.

See **[HOW_TO_INSTALL.md](./HOW_TO_INSTALL.md)** for the exact ScriptLoader path.

```
Scripts/Chess/
  ChessScene.cs           [CustomSceneEntry]  → SceneRegistry "ChessScene"
  ChessLaunchSystem.cs    [RegisterGameSystem] → SwitchSceneEvent on Play
  ChessBoardState.cs / ChessRules.cs / ChessAI.cs
```
