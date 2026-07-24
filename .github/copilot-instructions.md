# Remote Tech (Continued) Copilot Instructions

## Mod Overview and Purpose
Remote Tech (Continued) is an advanced content mod for RimWorld that expands gameplay through the introduction of remotely detonated explosives. The mod allows players to enhance their defense strategies, strategically mine resources, and more. Catering to both early and late-game colonies, the mod introduces a progression system starting with low-tech makeshift explosives and culminating with sophisticated chemical charges and automated defenses.

## Key Features and Systems
- **Explosive Crafting and Deployment**: 
  - Begin by researching 'Makeshift Explosives' to unlock basic explosives.
  - Craft initial explosives at the campfire or crafting spot using renewable materials.
  - Advanced explosives can be crafted at the explosives workshop or acquired from traders.
  - Use the build menu to place explosives, found under the Remote Tech category.

- **Detonation Systems**:
  - Initially, trigger explosives using a manual detonator connected by detonator wire.
  - Research the remote detonator to trigger explosives via radio signal.
  - Upgrade the remote detonator with channel capabilities for selective detonation control.

- **Research Progression**:
  - Follow the dedicated Remote Tech research tree to unlock advanced technologies and improved explosive capabilities.

- **Compatibility**: 
  - The mod can be safely incorporated into existing saves.

## Coding Patterns and Conventions
- **C# Practices**:
  - Follow standard RimWorld modding conventions with clear, descriptive function and variable names.
  - Ensuring all mod features are encapsulated within well-defined types and classes, making use of interfaces when logical separation is needed (e.g., `IRedButtonFeverTarget`, `ISwitchable`).
  - Implement error handling practices to maintain stability.

- **XML Integration**:
  - Definitions are organized logically by type (e.g., `DamageDef`, `KeyBindingDef`) and used to configure game elements such as damage types, apparel layers, designation categories, etc.
  - Keep XML clean and organized, using indentation and comments for readability.

## Harmony Patching
- Utilize the Harmony library to ensure compatibility and extend or modify core RimWorld functionality without altering the game's source code.
- Apply patches where necessary to override or augment game logic relating to detonation, crafting mechanics, or AI behavior associated with explosive operations.

## Suggestions for Copilot
- **Auto-completion for common tasks**:
  - Provide code suggestions for setting up new crafting recipes and detonator interactions.
  - Suggest efficient patterns for defining new explosives in XML.

- **Adaptable Code Generation**:
  - Generate template code for implementing new job drivers or AI behaviors related to the mod's features.
  - Recommend boilerplate Harmony patches for modifying vanilla methods affecting remote explosive mechanics.

- **Error Handling and Logging**:
  - Propose methods to integrate comprehensive error handling to log unexpected issues during mod execution.

- **Research and Progression Systems**:
  - Assist in generating research nodes and ensuring they are contextually linked within the Remote Tech research tree.

For more detailed support and feature discussions, please join the official forum or Discord channel provided by the mod community. Enjoy enhancing your RimWorld experience with Remote Tech's strategic depth!

## Project Solution Guidelines
- Relevant mod XML files are included as Solution Items under the solution folder named XML, these can be read and modified from within the solution.
- Use these in-solution XML files as the primary files for reference and modification.
- The `.github/copilot-instructions.md` file is included in the solution under the `.github` solution folder, so it should be read/modified from within the solution instead of using paths outside the solution. Update this file once only, as it and the parent-path solution reference point to the same file in this workspace.
- When making functional changes in this mod, ensure the documented features stay in sync with implementation; use the in-solution `.github` copy as the primary file.
- In the solution is also a project called Assembly-CSharp, containing a read-only version of the decompiled game source, for reference and debugging purposes.
- For any new documentation, update this copilot-instructions.md file rather than creating separate documentation files.


## Hard rules (must follow)
- Do NOT run commands that modify the repo (no git commit, git apply, dotnet format) unless explicitly asked.
- Prefer minimal reads: read only the smallest code region needed (around the suspicious lines).

