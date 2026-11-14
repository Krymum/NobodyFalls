# 11. Characters: a 2D rig and swappable part sets

- Status: accepted
- Date: 2026-11-14

## Context

Characters must have volume, lighting and five readable postures (held,
attention, danger, relieved, fallen), and arms that really pull on the ropes
(ART_DIRECTION, "Personnages"). Today the parts are drawn in code, reproducing
the validated reference scene; later an artist will deliver illustrated parts as
a sprite atlas. Skins are data (GAME_DESIGN §5). Changing the drawing must not
touch game logic nor the rest of the renderer.

## Decision

The `@nobodyfalls/render` package splits a character into four steps, each with
its own type:

1. **`solvePose(posture, phase, t) → Skeleton`**: pure. Joints (head, neck,
   shoulders, elbows, hands, hips, knees, feet, belt) in local space, solved with
   two-bone IK; head tilt, eyes, mouth; the pendulum sway, whose amplitude comes
   from the posture's token.
2. **`resolveLook(appearance, renderKeys) → Look`**: the appearance from the API
   (item ids and colours) becomes styles per layer, through the catalogue's
   `renderKey` (`coat.long`, `headwear.beanie`…). Unknown items fall back to plain
   styles, so a new catalogue item never breaks rendering.
3. **`PartSet.draw(ctx, layer, skeleton, look, mode)`**: draws one layer
   (`legs`, `back`, `torso`, `head`, `scarf`, `arms`, back to front) in local
   space. `CodePartSet` draws in code (lit volumes, rim light, neck occlusion), and
   a silhouette mode serves the far planes. An `AtlasPartSet` will draw sprites
   pinned to the same joints, with the lighting baked in. **This interface is the
   only thing that changes.**
4. **`CharacterRenderer`**: places the character (position, sway, scale), calls
   the part set layer by layer, and returns the scene **anchors** (hands, grip,
   belt) where ropes attach. `anchors()` can be called without drawing, so ropes
   are drawn behind the bodies.

Ropes (`RopeRenderer`: twisted fibres, tension → sag, thrown rope unrolling,
frayed thread, coil at the hip) and the scene (`SceneRenderer`) only consume
anchors and tokens. No renderer knows the rules of the game.

## Consequences

- Poses are unit-tested without a browser (limb lengths, grips, faces, sway
  ordering); drawing is tested with a recording context.
- The reference scene is kept as data (`REFERENCE_CAST`) and rebuilt with these
  renderers on the `/dev/scene` page, so any drift is visible next to the original.
