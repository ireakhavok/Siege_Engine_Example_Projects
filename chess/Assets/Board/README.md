# Board assets (optional)

Drop piece textures / meshes here and load them from `ChessScene` via
`ModelManager` or your sprite path.

Suggested names:

```
Assets/Board/
  square_light.png
  square_dark.png
  w_pawn.png … w_king.png
  b_pawn.png … b_king.png
```

Until assets exist, render with simple quads + text glyphs (as the web
reference does with Unicode pieces).
