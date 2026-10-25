# Random Superpower Battle Royale

> A first-person, cartoon-style multiplayer battle royale where your superpowers change every time you die.  
> Unity 6.3 (URP) · Photon Fusion 2 (Shared Mode) · Solo Development

<img width="1672" height="941" alt="Gameplay Screenshot" src="https://github.com/user-attachments/assets/5fb2760a-f84e-4fcc-8d59-adff66ad9678" />

---

## Overview

**Souls are both currency and the key to victory.**

Defeat enemies to collect Souls, then decide whether to spend them on upgrades in the shop or save them to win the game. Every life brings a different playstyle, as your randomly assigned superpower changes each time you die.

## Core Features

- **Souls = Currency + Win Condition** — Spend Souls to grow stronger, or save them to secure victory.
- **Three-Layer Ability System** — A primary ability (class) chosen at the start of each round, a secondary ability, and a random ability that changes with every death.
- **Wild and Wacky Random Abilities** — Examples include:
  - **"Let's Go to the Future!"** — Grab an opponent and charge straight off the map together.
  - **"Baldness Beam"** — Turn anyone you hit into an ally.
  - **"Legendary Dragon Chant"** — Recite the chant 30 times to transform into a dragon; survive for 3 minutes to win instantly.
  - **"Earth Toss"** — Hurl the entire Earth from 100 meters above the ground.
- **Transformations** — Completely change your appearance, stats, attacks, and abilities. Transformations include evolving from a fish into a dragon and promoting six chess pawns into a giant chess piece.
- **Environmental Kills & Knockback** — Knocking an opponent off the map counts as a kill for the player who pushed them.

## Major Systems

| Category | Features |
|---|---|
| Combat | Melee combos · Charged bows and staffs · Hitscan and projectile firearms · Ricocheting bullets · Attack speed and range buffs · Hit-stop |
| Abilities | Primary, secondary, and random abilities · Individual leveling for passives, skills, and ultimates · Skill mutations and slot swapping · Player-selectable skills |
| Status Effects | Slow · Burn · Freeze · Seal · Stun · Poison · Ghost · Darkness · Invisibility · Undead · Curse · Weaken and more |
| Equipment | C–S equipment tiers · Enchantments with tier-based tables and special effects · Weapon evolution · Loot chests · Shops |
| Communication | Text chat (match/team) · Voice chat (Photon Voice) · Global announcement messages |
| Rounds | Class selection → Combat → Win by reaching the Soul target → Full reset for the next round |

## Custom Unity Editor Tools

Developed custom editor tools to streamline animation, rigging, and visual effect workflows.

- **Motion Setup** — A first-person animation editor for arms, fingers, weapons, and the character's body. Features include a timeline, presets, shake and floating segments, hitbox visualization, event tracks, and weapon part configuration.
- **Hand Rig** — Finger IK and grip pose editing.
- **Rig Animator** — Bone hierarchy setup, IK, and state-based animation for summons, mounts, and transformation models.
- **VFX Studio** — A custom tool for creating cartoon-style skill visual effects.
- **AlterClipAnimator** — Plays animation clips (including Mixamo animations) by state name without requiring an Animator Controller, with support for hit-frame synchronization.

## Tech Stack

- Unity 6.3.21f1 · URP · Shader Graph toon shader
- Photon Fusion 2 — Shared Mode (each player has authority over their own character)
- Photon Voice 2
- Unity Localization — Google Sheets integration, with multilingual support planned

## Getting Started

1. Open the project using **Unity 6.3.21f1**.
2. **Obtain and configure your Photon App ID.** The App ID is not included in this repository.
   - Create a Fusion application in the [Photon Dashboard](https://dashboard.photonengine.com/) and copy its App ID.
   - In Unity, paste it into `Tools → Fusion → Fusion Hub` or the `PhotonAppSettings` asset.
   - To use voice chat, configure the Voice App ID in the same way.
3. Open the main scene at `Assets/(Scene Path)/(Main Scene).unity` and press Play.
4. To test multiplayer, add players through `Window → Multiplayer → Multiplayer Play Mode`, or run a build alongside the Unity Editor.

## Controls

| Key | Action |
|---|---|
| WASD · Space | Move · Jump |
| Left Mouse Button | Basic attack (hold and release for ranged attacks) |
| Q · E · R | Skill 1 · Skill 2 · Ultimate |
| F (V) | Random ability skill (Joker can also use V) |
| 1–7 · Mouse Wheel | Switch weapons |
| G | Drop weapon |
| Enter · Tab | Open chat · Switch chat channel |
| ` or F8 | Developer console (testing only) |

## Project Structure

```text
Assets/
 ├── Scripts/       Game logic (player, abilities, combat, UI, networking)
 ├── Editor/        Custom development tools (Motion Setup, Rig Animator, ability editor, etc.)
 ├── Data/          Ability, weapon, enchantment, and shop data (ScriptableObjects)
 └── ...
```

## Developer

- **Louis Jung (정루이)** — Game Design · Programming · Tool Development (Solo Developer)
- Portfolio: [https://louie0523.github.io](https://louie0523.github.io)
- Contact: louie07230723@gmail.com

## License

© 2026 Louis Jung. All Rights Reserved.

This repository is publicly available **for review and evaluation purposes only**. Copying, modifying, redistributing, or commercially using any code or assets from this repository is prohibited without permission.

Third-party resources remain subject to their respective original licenses.
