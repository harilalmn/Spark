---
id: concepts.design-language
title: Spark's visual design language
nodes: []
related: [concepts.lacing]
since: "0.1"
---

**Status:** Specification. Written before any UI code exists, and the UI is written to match it.
**Owner:** `spark-ui`
**Last updated:** 2026-08-28

> This topic is both an end-user reference — *why does Spark look like this?* — and the
> executable specification for the shell, the node canvas renderer and the viewport. Every
> colour on this page is a concrete hex value and every contrast ratio is a computed number,
> not an assertion. If the implementation and this page disagree, **the page is right**, and
> a pull request that lowers a ratio below its floor is rejected on that ground alone.

---

## The rule, before anything else

Spark's interface is a dark, soft, physical-feeling surface. Panels look raised. Wells look
carved. Buttons look pressed when you press them. That style is **neumorphism**, and it works
by rendering a control in *almost the same colour as the surface behind it*, distinguished
only by a paired light-and-dark shadow that suggests a lit, raised object.

That is also exactly why the style has a bad reputation. Low element-to-background contrast is
not a defect in careless implementations of neumorphism — it is the definition of the style.
So Spark draws a hard line through the middle of it:

> ### Neumorphism governs surfaces and depth. It never governs legibility.
>
> A soft shadow may say that a thing is **raised**, **pressed**, **inset** or **floating**.
>
> A soft shadow may never be the only signal for **text**, **icons**, **focus**, **selection**,
> **error state**, or **the boundary of a control you have to aim at**.
>
> Every one of those carries an independent, measurable contrast, and the numbers are in
> [§4](#4-contrast-rules-with-numbers).

The line is drawn there, and not further in, on purpose. A timid neumorphism that keeps every
element at high contrast is not a safe neumorphism — it is a flat theme with blurry edges, and
it has all of the cost and none of the character. Spark commits to real depth on surfaces:
±9 to ±19 units of L\* between a surface and its shadow pair, which on a dark ground is a great
deal. It is strict about where the depth stops.

---

## 1. Principles

**1. Depth is a surface property; legibility is a content property, and they are never traded
against each other.**
The moment a shadow is doing work that a contrast ratio should be doing, the design has failed,
because a shadow's readability depends on the monitor, the ambient light, the colour profile and
the viewer's eyes, and a contrast ratio does not.

**2. A state change may add a signal or raise contrast. It may never lower it.**
This is stricter than WCAG requires, and it is what makes a soft, shifting, hover-responsive
surface safe to use everywhere without auditing each case individually — the audit is discharged
once, by the rule.

**3. Colour is never the only carrier of meaning.**
Every semantic state pairs a colour with a glyph, a stroke, a position or a word. Roughly one in
twelve of Spark's male users has a colour-vision deficiency, and every user has a bad monitor
some of the time.

**4. Two roles, two vocabularies: fills identify, strokes report.**
A node's fill says *what kind of node this is* (its library category). A stroke, ring or badge
says *what is happening to it* (selected, evaluating, errored, frozen). Because the two never
share a pixel role, a green node body and a green wire can coexist without either becoming
ambiguous.

**5. The canvas is drawn by hand, so its style must survive being drawn by hand at 2,000 nodes
and 60 fps.**
[ADR-0013](../../adr/0013-immediate-mode-node-canvas.md) makes the canvas one immediate-mode
control over a spatial index, and drops to a level-of-detail representation below 40% zoom. Every
canvas cue in this document declares the zoom at which it is dropped and what carries its meaning
afterwards.

**6. Restraint is the aesthetic.**
Spark is a tool people stare at for eight hours while thinking about something else. Motion is
short, colour is used sparingly, and nothing moves that the user did not move.

---

## 2. The palette

### 2.1 One theme in v1

**Spark v1 ships a dark theme only.** There is no light theme, no automatic theme switching and
no user colour customisation in 0.1. Shipping one theme properly is worth more than shipping two
badly, and a neumorphic system is the worst possible candidate for a naive colour inversion — see
[§2.7](#27-what-a-light-theme-would-cost).

Token names are deliberately theme-neutral (`surface.base`, not `grey.800`) precisely so that a
second theme is a value swap rather than a rename.

### 2.2 The surface ladder

Every surface in the product is one of these, and every interactive surface has a **state ladder**
of successively darker steps used by hover, press and selection (see
[§5](#5-interaction-states)). Steps are roughly −3 L\* apart.

| Token | Rest | −1 (hover) | −2 (pressed / selected) | L\* at rest | Used for |
|---|---|---|---|---|---|
| `bg.void` | `#12151A` | — | — | 6.68 | Window chrome behind panels, dock gutters, splitters |
| `canvas.bg` | `#171B21` | — | — | 9.61 | The node canvas ground |
| `canvas.group` | `#1E222A` | — | — | 13.15 | Group frame fill on the canvas |
| `surface.sunken` | `#1A1E24` | `#161A1F` | `#12151A` | 11.10 | Inset wells: text fields, search boxes, slider tracks, list backgrounds |
| `surface.base` | `#23272F` | `#1D2128` | `#181C22` | 15.56 | Panel bodies, the library, the properties inspector, list rows |
| `node.body` | `#262B33` | `#20242B` | `#1B1F26` | 17.36 | The body of a node on the canvas |
| `surface.raised` | `#2A2F38` | `#232830` | `#1C2027` | 19.27 | Buttons, toolbar chips, tabs, cards |
| `surface.float` | `#2E3440` | `#272D37` | `#212630` | 21.61 | Menus, popups, dialogs, tooltips, drag ghosts |

Read the ladder in the correct direction. Hover and press make a surface **darker**, not lighter.
That is unusual, it is deliberate, and [§5.1](#51-the-hover-rule-and-why-it-inverts-the-usual-move)
explains why it is the only direction that satisfies Principle 2 on a dark theme.

### 2.3 Depth: highlight and shadow pairs

These are the neumorphic pairs. They are **decorative** — none of them is permitted to be the sole
boundary of anything (Principle 1).

| Token | Hex | L\* | ΔL\* from its surface | Role |
|---|---|---|---|---|
| `depth.hi.base` | `#343B47` | 24.68 | **+9.12** | Lit side, elevation 2 on `surface.base` |
| `depth.lo.base` | `#0C0E13` | 3.97 | **−11.59** | Shadow side, elevation 2 on `surface.base` |
| `depth.hi.raised` | `#3C4452` | 28.65 | **+9.38** | Lit side, elevation 2 on `surface.raised` |
| `depth.lo.raised` | `#0C0E13` | 3.97 | **−15.30** | Shadow side, elevation 2 on `surface.raised` |
| `depth.hi.float` | `#414A59` | 31.21 | **+9.61** | Lit side, elevation 3 |
| `depth.lo.float` | `#07080B` | 2.20 | **−19.41** | Shadow side, elevation 3 |
| `lip.rest` | `#3E4654` | 29.52 | +10.25 over raised | The 1 px lit lip along a raised control's top and left edges |
| `lip.hover` | `#8674D6` | 54.46 | — | The lip on hover; 3.86:1 against `surface.raised`−1, so it is a real signal |

### 2.4 Text

| Token | Hex | Role |
|---|---|---|
| `text.primary` | `#F2F5FA` | Body copy, node titles, values, everything you read to do your job |
| `text.secondary` | `#C6CDD9` | Labels, port names, column headers, supporting copy |
| `text.muted` | `#A4ADBB` | Units, counts, placeholder text, timestamps |
| `text.disabled` | `#949DAC` | Text inside a disabled control — **still above 4.5:1 everywhere** |
| `text.inverse` | `#141821` | Dark text on a bright fill: node headers, accent buttons, semantic badges |

`text.disabled` is only one step below `text.muted`, because Spark does not signal
disabledness by fading text into the background. See [§5.5](#55-disabled).

### 2.5 Borders, accent and semantics

| Token | Hex | Role |
|---|---|---|
| `border.hairline` | `#343A45` | Decorative dividers and separators only. **Never a control boundary.** 1.30:1 on `surface.base` and that is fine, because it is never load-bearing. |
| `border.control` | `#7C8595` | The boundary token. Every control whose extent must be judged, and every node outline. ≥3.35:1 against every surface in the palette. |
| `border.strong` | `#9AA2B1` | Table rules, the active dock edge, anything that needs to read as structural. |
| `accent` | `#A98BFF` | Selection, focus, active tab, primary action, the evaluating indicator |
| `accent.hover` | `#C0A8FF` | Accent fill under a pointer |
| `accent.press` | `#D4C4FF` | Accent fill while pressed |
| `focus.ring` | `#CDBCFF` | The keyboard focus ring |
| `focus.contour` | `#0C0E13` | The 1 px dark separator on both sides of the focus ring |
| `state.error` | `#FF7B82` | Errors, refused connections |
| `state.warning` | `#F0A63C` | Warnings, lossy conversions |
| `state.success` | `#5FD39A` | Success, valid connections |
| `state.info` | `#68B6F2` | Information, hints |

> **Decision V1 — the type-compatibility wire colours are the semantic tokens, not a
> parallel set.**
> When you drag a wire, the graph engine reports whether the connection is accepted, accepted
> with a lossy conversion, or refused. Those three states are drawn in `state.success`,
> `state.warning` and `state.error` — the same three hexes used by node error badges and
> diagnostic messages. The rejected alternative was a fourth, wire-only ramp of green/amber/red
> chosen to sit "beside" the semantics without colliding. It was rejected because two nearly
> identical reds in one product is worse than one red used in two roles: users would have to
> learn which red meant what, and the near-match would read as a rendering bug. Principle 4
> makes the reuse safe — semantic colours appear only on **strokes, rings, glyphs and message
> text**, never as a fill, and the three drag colours appear only on the wire being dragged and
> the port under the cursor, only while the mouse button is down. A cursor glyph (`✓`, `≈`, `✕`)
> accompanies each, so the meaning survives colour blindness and monochrome capture.

### 2.6 The accent hue is substitutable at fixed luminance

`accent` is the one token in this document that is a brand choice rather than a technical one.
Violet was chosen because it is the only saturated hue that does not land inside the red–amber–
green–blue bands already claimed by the semantic set, and because a calm accent keeps the loud
colours available for things that are actually urgent.

If the brand wants a different hue, **substitute one of these and not a single ratio in this
document changes**, because they are matched on relative luminance:

| Option | Hex | Relative luminance Y | On `surface.base` | On `canvas.bg` | `text.inverse` on it |
|---|---|---|---|---|---|
| **Violet (chosen)** | `#A98BFF` | 0.3412 | 5.57:1 | 6.43:1 | 6.61:1 |
| Cyan | `#2FB4C4` | 0.3723 | 6.02:1 | 6.95:1 | 7.14:1 |
| Magenta | `#EE79BE` | 0.3557 | 5.78:1 | 6.67:1 | 6.86:1 |
| Amber | `#CE9C36` | 0.3717 | 6.01:1 | 6.93:1 | 7.13:1 |

The constraint on any substitute is stated as a number rather than a taste: **Y between 0.33 and
0.38, and a hue at least 40° away from every semantic hue.** Amber is inside that band on
luminance but fails the hue clause against `state.warning`; it is listed for completeness and
should not be chosen.

### 2.7 What a light theme would cost

Not an inversion. Specifically:

- **The depth budget flips sides.** On a light base the *shadow* has all the headroom and the
  *highlight* has almost none, which is the exact mirror of [§2.8](#28-the-hard-part-neumorphic-depth-on-a-dark-ground).
  The 1 px lit lip would become a 1 px shadowed underhang, and the blur radii would shrink.
- **Every chromatic token must be re-picked, not darkened.** `accent` at 5.57:1 on `#23272F`
  gives about 1.9:1 on a light `#ECEFF4`. Accent, all four semantics and all ten category colours
  need new values, and the category set is the hard one because a light theme compresses the
  usable luminance band for ten mutually distinguishable hues.
- **The wire casing and core swap roles** ([§7.5](#75-wires)): a light casing under a dark core.
- **The hover direction reverses.** On light surfaces with dark text, hover must *lighten*
  ([§5.1](#51-the-hover-rule-and-why-it-inverts-the-usual-move) explains the invariant that both
  directions serve).
- **The 3D viewport needs its own decision**, because a light ground changes the shading model,
  not just the background colour.

That is a body of work, not a stylesheet. It is out of scope for 0.1 and this section exists so
that the size of it is known before someone promises it.

### 2.8 The hard part: neumorphic depth on a dark ground

This is the single practical problem that makes dark neumorphism harder than light neumorphism,
and a specification that skips it is decoration rather than engineering.

**The shadow is the constrained side, not the highlight.** The intuition runs the other way —
"there is no room to go lighter on a dark theme" — and it is wrong. The classic light-neumorphic
base is around `#E0E5EC`, at L\* ≈ 90: the highlight has about 10 units of L\* above it and the
shadow has about 90 below. Spark's base is at L\* 15.56: the highlight has about 84 units above
it and the shadow has **15.56**. Depth is read primarily from occlusion — from the dark side —
so dark neumorphism starves the cue that actually does the work.

Three consequences follow, and each has a countermeasure written into this specification.

**(a) The base must be lifted off black.** `surface.base` is `#23272F`, not `#121212`. That is
deliberate and it costs contrast against text, which is why the base sits where it does and not
higher. The rule is: **a surface that carries elevation must leave at least 10 units of L\*
between itself and the shadow floor.** `surface.base` leaves 11.59, `surface.raised` leaves 15.30,
`surface.float` leaves 19.41. `canvas.bg` and `bg.void` deliberately carry *no* elevation of their
own, which is what lets them sit lower.

**(b) The shadow must not bottom out at pure black.** `depth.lo.float` is `#07080B` (L\* 2.20),
not `#000000`. A pure-black shadow on an OLED or a well-calibrated panel reads as a hole punched
through the interface rather than as an occluded surface, and it also removes the last of the
gradient's room to fall off. The floor is L\* ≥ 2.

**(c) The lit side has to stop being a blur and become a line.** A wide, soft, light-on-dark
highlight does not read as a lit surface; it reads as an emissive rim — a glow — because matte
dark materials do not produce broad specular highlights in the real world. Worse, a glow is
exactly the visual language reserved for focus, so a big highlight competes with the one cue that
must never be ambiguous. So Spark spends almost all of the lit-side budget on a **1 px lip**
(`lip.rest`, `#3E4654`, +10.25 L\*) along the top and left edges of a raised surface, and only a
narrow 6 px blur behind it. Crisp lines read as lit edges; broad ones read as lamps.

There is a fourth problem that is purely technical: **banding**. A blurred shadow falling from
`#0C0E13` to `#23272F` traverses about 23 8-bit code values over 12 px. That is visibly stepped
on a large surface. Every shadow gradient in Spark is dithered with 1.5% monochrome noise applied
at composite time; this is cheap, invisible as texture, and removes Mach banding entirely.

The pleasing part of (c) is that it is also the fix for the interaction problem. Because the
lit-side budget lives in a 1 px line rather than in the surface fill, **hover can brighten the
lip without brightening anything that sits behind text** — which is precisely what
[§5.1](#51-the-hover-rule-and-why-it-inverts-the-usual-move) needs.

---

## 3. Elevation

Four levels. Not five, not nine.

Offsets and blurs are in device-independent pixels at 100% scale. The light source is fixed at
the **top-left**, so lit edges are up-and-left and shadows fall down-and-right, everywhere, with
no exceptions. Blur radii are drawn from the fixed set **{5, 6, 12, 16, 28}** so that the canvas
renderer can cache shadow sprites per size bucket rather than running a real Gaussian blur per
node — at 2,000 nodes the difference is the frame budget.

### E0 — Flat

No shadow, no lip. The surface is simply a fill.

Used by: `canvas.bg`, `bg.void`, panel backgrounds, table rows at rest, **every disabled control**,
and every canvas element below 73% zoom.

### E1 — Inset

The surface is carved into its parent.

| Layer | Offset | Blur | Colour | Alpha |
|---|---|---|---|---|
| Inner shadow | +2, +2 | 5 | `depth.lo.base` `#0C0E13` | 75% |
| Inner highlight | −2, −2 | 5 | `depth.hi.base` `#343B47` | 45% |

Used by: text fields, the search box, slider tracks, the list well, tab strips, the **pressed**
state of any E2 control, and the **off** state of the node preview toggle.

### E2 — Raised

The default for anything you can click.

| Layer | Offset | Blur | Colour | Alpha |
|---|---|---|---|---|
| Shadow | +3, +4 | 12 | `depth.lo.raised` `#0C0E13` | 75% |
| Highlight | −2, −2 | 6 | `depth.hi.raised` `#3C4452` | 55% |
| Lip | 1 px inner line, top and left edges | — | `lip.rest` `#3E4654` | 70% |

Used by: buttons, toolbar chips, cards, the selected tab, and **every node on the canvas at
≥82% zoom**.

### E3 — Floating

Detached from the layout; something opened over the top of the interface.

| Layer | Offset | Blur | Colour | Alpha |
|---|---|---|---|---|
| Ambient shadow | +4, +10 | 28 | `depth.lo.float` `#07080B` | 80% |
| Contact shadow | +1, +2 | 5 | `depth.lo.float` `#07080B` | 60% |
| Lip | 1 px inner line, top and left | — | `depth.hi.float` `#414A59` | 60% |
| Outline | 1 px | — | `border.hairline` `#343A45` | 100% |

Used by: menus, context menus, the node library flyout, dialogs, tooltips, autocomplete popups,
and the drag ghost of a node being dragged out of the library.

E3 is the only level with an outline in its base definition, because a floating surface can land
on top of anything — including a bright node header — and the shadow alone cannot be relied on
to separate it from an arbitrary backdrop.

---

## 4. Contrast rules, with numbers

### 4.1 The floors

| What | Floor | Source |
|---|---|---|
| Body text, and any text below 18 px regular / 14 px bold | **4.5:1** | WCAG 2.2 AA, SC 1.4.3 |
| Large text: ≥18 px regular or ≥14 px semibold | **3:1** | WCAG 2.2 AA, SC 1.4.3 |
| Icons and glyphs that carry meaning | **3:1** | WCAG 2.2 AA, SC 1.4.11 |
| The boundary of a control whose extent must be judged | **3:1** | WCAG 2.2 AA, SC 1.4.11 |
| Focus indicator, against **both** of its neighbours | **3:1** | WCAG 2.2 AA, SC 1.4.11 / 2.4.13 |
| Node fill against the canvas at LOD | **3:1** | Local rule; a node is a control |
| A wire against whatever it crosses | **3:1** | Local rule |

**Spark applies the 4.5:1 body floor to large text as well.** The 3:1 allowance exists because
big glyphs have thicker strokes; but Spark's largest routine text is 15 px, the scale tops out at
28 px for empty states, and the saving would apply almost nowhere. Holding one number is simpler
to review than holding two, and no pairing in this palette needed the relaxation.

**`text.disabled` is held to 4.5:1 as well.** WCAG exempts disabled controls. Spark does not take
the exemption, because "you cannot use this" and "you cannot read this" are different statements
and users routinely need to read a disabled field to work out how to enable it.

### 4.2 What the floors do *not* cover

Stated up front so it is a scope definition and not a later excuse. Three categories are exempt,
each with a replacement guarantee:

- **Decorative dividers** (`border.hairline`, 1.30:1 on `surface.base`). Exempt because they
  separate content that is already separated by layout. They may never be the only boundary of an
  interactive control; `border.control` exists for that.
- **Depth pairs and lips** (`lip.rest`, 1.41:1 on `surface.raised`). Exempt by Principle 1 — they
  are the style, not the signal. Every control they decorate is identifiable without them.
- **Shaded 3D geometry and the viewport grid.** The viewport renders a scene, not a document.
  `grid.minor` sits at 1.26:1 against the viewport background on purpose. Everything *overlaid*
  on the scene — selection outlines, axis labels, dimension text, warnings — is UI and is fully
  in scope. The single deliberate exception inside the scene is ghosted geometry, and
  [§8.4](#84-ghosted-geometry-the-one-declared-exception) states it as an exception with its
  number and its second signal.

### 4.3 Text on every surface state

Every number computed from the sRGB relative-luminance formula and truncated downward, so a
figure printed here is never rounded up into passing. **Floor 4.5:1. Nothing in this table is
below it.**

| Surface | Hex | `text.primary` | `text.secondary` | `text.muted` | `text.disabled` |
|---|---|---|---|---|---|
| `bg.void` | `#12151A` | 16.74 | 11.44 | 8.07 | 6.68 |
| `canvas.bg` | `#171B21` | 15.81 | 10.80 | 7.63 | 6.31 |
| `canvas.group` | `#1E222A` | 14.58 | 9.97 | 7.03 | 5.82 |
| `surface.sunken` | `#1A1E24` | 15.31 | 10.46 | 7.38 | 6.11 |
| `surface.sunken` −1 | `#161A1F` | 15.99 | 10.93 | 7.71 | 6.38 |
| `surface.sunken` −2 | `#12151A` | 16.74 | 11.44 | 8.07 | 6.68 |
| `surface.base` | `#23272F` | 13.69 | 9.36 | 6.61 | 5.47 |
| `surface.base` −1 | `#1D2128` | 14.77 | 10.10 | 7.13 | 5.90 |
| `surface.base` −2 | `#181C22` | 15.64 | 10.69 | 7.55 | 6.25 |
| `node.body` | `#262B33` | 13.02 | 8.90 | 6.28 | 5.20 |
| `node.body` −1 | `#20242B` | 14.24 | 9.73 | 6.87 | 5.69 |
| `node.body` −2 | `#1B1F26` | 15.12 | 10.33 | 7.29 | 6.04 |
| `surface.raised` | `#2A2F38` | 12.30 | 8.40 | 5.93 | 4.91 |
| `surface.raised` −1 | `#232830` | 13.55 | 9.26 | 6.54 | 5.41 |
| `surface.raised` −2 | `#1C2027` | 14.95 | 10.22 | 7.21 | 5.97 |
| `surface.float` | `#2E3440` | 11.42 | 7.81 | 5.51 | **4.56** |
| `surface.float` −1 | `#272D37` | 12.66 | 8.66 | 6.11 | 5.06 |
| `surface.float` −2 | `#212630` | 13.87 | 9.48 | 6.69 | 5.54 |
| `viewport.top` | `#1B1F26` | 15.12 | 10.33 | 7.29 | 6.04 |

**The lowest text ratio anywhere in the palette is 4.56:1** — `text.disabled` on `surface.float`,
which is a disabled item in an open menu. It clears the floor by 0.06. That pairing is the binding
constraint on the whole palette: it is why `surface.float` is `#2E3440` and not lighter, and why
`text.disabled` is `#949DAC` and not dimmer.

### 4.4 Accent and semantic colours used as text or glyphs

Floor 4.5:1.

| Token | Hex | on `canvas.bg` | on `surface.sunken` | on `surface.base` | on `surface.raised` | on `surface.float` | on `node.body` |
|---|---|---|---|---|---|---|---|
| `accent` | `#A98BFF` | 6.43 | 6.23 | 5.57 | 5.00 | **4.65** | 5.30 |
| `accent.hover` | `#C0A8FF` | 8.46 | 8.19 | 7.33 | 6.58 | 6.11 | 6.97 |
| `focus.ring` | `#CDBCFF` | 10.06 | 9.74 | 8.72 | 7.83 | 7.27 | 8.28 |
| `state.error` | `#FF7B82` | 6.91 | 6.69 | 5.99 | 5.38 | 5.00 | 5.69 |
| `state.warning` | `#F0A63C` | 8.41 | 8.14 | 7.28 | 6.54 | 6.08 | 6.92 |
| `state.success` | `#5FD39A` | 9.27 | 8.98 | 8.03 | 7.21 | 6.70 | 7.63 |
| `state.info` | `#68B6F2` | 7.86 | 7.61 | 6.81 | 6.12 | 5.68 | 6.47 |

### 4.5 Dark text on bright fills

Floor 4.5:1. `text.inverse` is `#141821`.

| Fill | Hex | Ratio |
|---|---|---|
| `accent` | `#A98BFF` | 6.61 |
| `accent.hover` | `#C0A8FF` | 8.69 |
| `accent.press` | `#D4C4FF` | 11.11 |
| `state.error` | `#FF7B82` | 7.11 |
| `state.warning` | `#F0A63C` | 8.64 |
| `state.success` | `#5FD39A` | 9.53 |
| `state.info` | `#68B6F2` | 8.08 |

Every node header is also a bright fill carrying `text.inverse`; those ten ratios are in
[§7.2](#72-category-colour-identity).

### 4.6 Non-text boundaries

Floor 3:1.

| Token | Hex | `canvas.bg` | `surface.sunken` | `surface.base` | `surface.raised` | `surface.float` | `node.body` |
|---|---|---|---|---|---|---|---|
| `border.control` | `#7C8595` | 4.64 | 4.49 | 4.02 | 3.61 | **3.35** | 3.82 |
| `border.strong` | `#9AA2B1` | 6.72 | 6.51 | 5.82 | 5.23 | 4.86 | 5.54 |
| `accent` | `#A98BFF` | 6.43 | 6.23 | 5.57 | 5.00 | 4.65 | 5.30 |
| `focus.ring` | `#CDBCFF` | 10.06 | 9.74 | 8.72 | 7.83 | 7.27 | 8.28 |

**Where `border.control` is required, and where it is not.** SC 1.4.11 requires a 3:1 boundary
where the boundary is needed to *identify* the control. A button with a visible text label is
identified by its label, so a labelled button on a panel carries no border and lets the E2 depth
pair do the aesthetic work — this is where the neumorphism actually lives, and it is not a
compromise, it is the style working as intended.

`border.control` is mandatory on:

- icon-only buttons, which have no label to identify them;
- empty or placeholder-only text fields, combo boxes and search boxes;
- checkboxes, radio buttons, toggles and the node preview toggle;
- **every node on the canvas**, because a node's extent is what you aim at, drag by and connect
  to, and `node.body` sits at only 1.21:1 against `canvas.bg`;
- any control that lands on `surface.float`, because the backdrop of a floating layer is not
  known in advance.

That node-outline requirement is the largest single concession the neumorphic aesthetic makes in
this document. It is recorded as a decision in [§13](#13-decisions-taken-in-this-document).

---

## 5. Interaction states

### 5.1 The hover rule, and why it inverts the usual move

Principle 2 says a state change may not lower contrast. On a dark theme carrying light text, the
usual dark-theme hover — *brighten the surface* — does exactly that: it moves the surface towards
the text and the ratio falls. The usual neumorphic hover has the same problem for the same reason.

So the rule is stated in a form that is direction-independent:

> **Hover moves a surface *away* from the colour of the text on it. Never towards it.**
>
> - A dark surface with light text gets **darker** on hover.
> - A bright fill with dark text (accent buttons, node headers, semantic badges) gets
>   **brighter** on hover.
>
> In both cases the ratio rises. There is no third case.

The felt sense of "coming forward" is then supplied by the two things that are *not* behind text:
the **lip** brightens from `lip.rest` `#3E4654` to `lip.hover` `#8674D6`, and the **elevation**
increases — the E2 shadow offset grows from +3,+4 to +4,+6 and its blur from 12 to 16. That is
physically coherent as well: lifting an object off a surface casts a longer shadow and does not
require the object's own face to catch more light.

**Explicitly forbidden**, in review, in code, in design files:

- darkening a surface *towards* the text on it (light text on a lightening surface, or dark text
  on a darkening one);
- lowering text opacity to signal any state whatsoever;
- tinting a fill with `accent` or a semantic colour underneath text — see
  [§5.4](#54-selected) for what to do instead;
- signalling hover only by a change in blur radius, which is invisible at small sizes and on
  low-DPI panels.

### 5.2 The full state matrix

For every interactive element. "L−1", "L−2" refer to the state ladder in
[§2.2](#22-the-surface-ladder).

| Element | Rest | Hover | Pressed | Focused | Selected | Disabled | Error |
|---|---|---|---|---|---|---|---|
| **Secondary button** | E2 on `surface.raised`, `text.primary` | fill → L−1, lip → `lip.hover`, shadow +33% | E1 inset, fill → L−2 | + focus sandwich ([§6](#6-keyboard-focus)) | n/a | E0 flat, fill `surface.sunken`, `text.disabled`, no hover response | 1 px `state.error` outline + `✕` glyph before the label |
| **Primary button** | E2, `accent` fill, `text.inverse` | fill → `accent.hover` (6.61 → 8.69) | E1 inset, fill → `accent.press` (→ 11.11) | + focus sandwich | n/a | E0 flat, `surface.sunken`, `text.disabled` — **accent is removed entirely**, never faded | inherits secondary |
| **Icon button** | E2 + `border.control` | as secondary; icon → `text.primary` | as secondary | + focus sandwich | E1 inset + 2 px `accent` bottom bar (toggle-on) | E0 flat, icon `text.disabled` | `state.error` outline |
| **Text field** | E1 on `surface.sunken` + `border.control`, `text.primary` | fill → L−1, border → `border.strong` | n/a | focus sandwich **replaces** the border; caret `accent` | text selection: `accent` fill, `text.inverse` | E0 flat, no border, `text.disabled` | border → 2 px `state.error`, message below in `state.error` + `✕` glyph |
| **List row** (library, watch, diagnostics) | E0 on `surface.base` | fill → L−1 (9.36 → 10.10 for `text.secondary`) | fill → L−2 | focus sandwich, inset 1 px | fill → L−2 **plus a 3 px `accent` spine** on the leading edge; label → weight 500 | `text.disabled`, no hover response, no spine | leading `state.error` glyph + `state.error` label |
| **Menu item** | E0 on `surface.float` | fill → L−1 (11.42 → 12.66) | fill → L−2 | focus sandwich | check glyph in `accent` | `text.disabled` (4.56:1), no hover | n/a |
| **Tab** | E0, `text.secondary`, no depth | fill → L−1, text → `text.primary` | fill → L−2 | focus sandwich | E2 raised, fill `surface.raised`, `text.primary`, 2 px `accent` bottom bar | `text.disabled`, no hover | 6 px `state.error` dot after the label |
| **Node** | E2, `node.body`, category header | body → L−1, header → +14% white, lip → `lip.hover`, ports grow 5→7 px | n/a (drag begins) | focus sandwich outside the node outline **plus corner ticks** | 2 px `accent` ring + body → L−1 + header underline | frozen, not disabled — see [§7.7](#77-frozen-preview-off-and-not-evaluated) | 2 px `state.error` ring + `✕` glyph in the header + badge |
| **Port** | 5 px disc, `port.rest` `#8A93A2` (4.59:1 on `node.body`) | 7 px, 2 px `accent` ring, 14 px hit target | wire begins | 2 px `accent` ring + focus sandwich | ring in `accent` | n/a | 2 px `state.error` ring on a required-but-empty input |
| **Wire** | 1.75 px `wire.core` over 3.75 px `wire.casing` | core → 2.25 px, `accent.hover` | n/a | focus sandwich along the path | core → `accent`, 2.5 px | greyed with the not-evaluated node it feeds | `state.error` core |

### 5.3 Worked hover ratios

The point of the rule is that it is checkable. These are the before-and-after numbers for the
state transitions that actually occur, computed the same way as everything else.

| Element | Text token | Rest | Hover | Pressed / selected | Direction |
|---|---|---|---|---|---|
| Toolbar button label | `text.primary` on `surface.raised` | 12.30 | **13.55** | **14.95** | rises |
| Library list row | `text.secondary` on `surface.base` | 9.36 | **10.10** | **10.69** | rises |
| Library row, secondary line | `text.muted` on `surface.base` | 6.61 | **7.13** | **7.55** | rises |
| Menu item | `text.primary` on `surface.float` | 11.42 | **12.66** | **13.87** | rises |
| Node title | `text.primary` on `node.body` | 13.02 | **14.24** | **15.12** | rises |
| Node port label | `text.secondary` on `node.body` | 8.90 | **9.73** | **10.33** | rises |
| Text field value | `text.primary` on `surface.sunken` | 15.31 | **15.99** | **16.74** | rises |
| Primary button label | `text.inverse` on `accent` | 6.61 | **8.69** | **11.11** | rises |
| Node header title (`Point.ByCoordinates`) | `text.inverse` on `cat.point` | 6.58 | **7.66** | — | rises |
| Node header title (`Math.Sin`) | `text.inverse` on `cat.math` | 5.96 | **6.99** | — | rises |

Not one of them falls. That is the whole design of the state ladder.

### 5.4 Selected

Selection never tints a fill that sits behind text. `accent` at 14% over `surface.base` produces
`#36354C`, on which `text.primary` reads 10.85:1 — down from 13.69:1 at rest. It is still far
above the floor, and it is still forbidden, because Principle 2 is an invariant and not a budget.

Selection is instead signalled by three stacked cues:

1. the fill steps **down** the ladder (contrast rises);
2. a **3 px `accent` spine** on the leading edge for rows, or a **2 px `accent` ring** for nodes
   and canvas objects — 6.37:1 against the selected row fill, 5.30:1 against `node.body`;
3. the label moves from weight 400 to weight 500.

Accent tints are permitted only where there is no text over them: the marquee rectangle on empty
canvas, the filled portion of a slider track, and progress fills.

**Multi-selection** uses the same ring on every member. The **anchor** — the item keyboard
operations act from, and the one whose properties the inspector shows — additionally carries
four 6 px `accent` corner ticks. Ticks were chosen over a brighter ring because they are a
*shape* difference: they survive monochrome rendering, colour blindness and a bad monitor.

### 5.5 Disabled

Disabled is signalled by **the removal of depth**, not by the removal of contrast.

- Elevation drops to **E0**. Nothing raised, nothing inset, no lip. In a system where depth means
  interactive, flatness reads immediately as "not that".
- The fill becomes `surface.sunken`; text becomes `text.disabled`, which is still ≥4.56:1
  everywhere.
- Accent and semantic colour is **removed**, not faded — a disabled primary button is a flat grey
  button, not a translucent violet one.
- There is **no hover response at all**, which is the single clearest confirmation a pointer user
  can get.
- The cursor becomes `not-allowed`, and the control is removed from the tab order but keeps its
  accessible name and a `disabled` state so a screen reader announces it.

Five independent signals, none of them a contrast reduction.

### 5.6 Error

Error is never colour alone. Every error state carries **all three** of:

1. a 2 px `state.error` stroke on the control's boundary (5.00–6.91:1 against every surface);
2. a `✕` glyph, drawn as a shape and not as a font glyph so it survives font fallback;
3. a message in words, in `state.error` on the surface, at ≥5.00:1.

Warnings use `state.warning` and a `⚠` glyph and are otherwise identical. This mirrors the engine:
[`concepts.lacing`](lacing.md) §2.12 distinguishes an Error, which produces no output, from a
Warning, which produces output with caveats, and the interface must make the same distinction
visible without the user having to read the diagnostic code.

---

## 6. Keyboard focus

A focus ring drawn as a soft glow is invisible to exactly the people who need it, on exactly the
displays they are most likely to be using. Spark's focus indicator is a hard geometric shape with
a guaranteed ratio on both of its sides.

**The focus sandwich.** 4 px total, drawn **outside** the control's bounds with a 2 px gap:

| Layer | Width | Colour |
|---|---|---|
| Inner separator | 1 px | `focus.contour` `#0C0E13` |
| Ring | 2 px | `focus.ring` `#CDBCFF` |
| Outer separator | 1 px | `focus.contour` `#0C0E13` |

The two dark separators exist so that the ring's 3:1 requirement holds regardless of what it
touches. Against `focus.contour` the ring reads **11.24:1**; `focus.contour` reads **7.19:1**
against an `accent` fill, **11.46:1** against the brightest node header, and **17.66:1** against
`text.primary`. So a focused button on a panel, a focused item inside a selected accent row, and
a focused port on a gold `Input` node are all covered by the same four pixels, with no per-case
reasoning.

The ring alone, without the sandwich, already reads 7.27–10.06:1 against every surface in the
palette ([§4.6](#46-non-text-boundaries)). The sandwich exists for the arbitrary backdrops:
node headers, geometry in the viewport, and floating layers.

**Rules that go with it:**

- **Focus is independent of hover.** A hovered control shows its hover state; a focused control
  shows the ring; a hovered *and* focused control shows both. Neither substitutes for the other,
  and moving the mouse never removes a keyboard focus ring.
- **Focus is independent of selection.** In the library, `Down` moves focus without changing
  selection; the focused row shows the ring, the selected row shows the accent spine, and one row
  can show both.
- **Focus is never shown by colour alone.** It has a position (outside the bounds), a geometry
  (4 px, hard-edged, following the control's corner radius) and a value.
- **Focus is never shown by a glow, a blur, a shadow or an elevation change.** Listed as a
  prohibition in [§11](#11-what-this-style-is-not-allowed-to-do) because it is the specific
  failure this section exists to prevent.
- **Focus is visible on first `Tab`.** There is no focus-visible heuristic that hides the ring
  from pointer users and then fails to bring it back.
- **On the canvas**, where there are no Avalonia controls to inherit focus behaviour from
  (ADR-0013), the renderer draws the same sandwich around the focused node, port or wire, and
  scrolls it into view. This is explicitly part of the M8 accessibility pass and is specified
  here so that pass has something to implement rather than to invent.

---

## 7. The node canvas

The hardest surface in the product, because it is drawn by our own renderer rather than by
Avalonia (ADR-0013), because several hundred nodes are on screen at once, and because below 40%
zoom nodes degrade to plain category-coloured rectangles with no text at all.

### 7.1 Node anatomy

```text
        ┌───────────────────────────────────────┐   ← 1 px border.control, 6 px radius
        │  ◈  Point.ByCoordinates          ⚠ ⏸  │   ← header: FULL category colour,
        ├───────────────────────────────────────┤     text.inverse, 22 px, glyphs right
     ●──┤ x                                     │   ← body: node.body, text.secondary
     ◎──┤ y                              output ├──●     port labels, 11 px
     ●──┤ z                                     │
        │                              ◉ preview│   ← preview toggle, E2 on / E1 off
        └───────────────────────────────────────┘
              ╲ E2 shadow, +3/+4, blur 12 ╱
```

| Part | Height / size at 100% | Fill | Text |
|---|---|---|---|
| Header | 22 px | full category colour | `text.inverse`, 12 px / 600 |
| Body | content | `node.body` `#262B33` | `text.secondary`, 11 px / 400 |
| Outline | 1 px | `border.control` `#7C8595` | — |
| Ports | 5–7 px | `port.rest` / `port.connected` | — |
| Preview toggle | 14 px pill | E2 raised (on) / E1 inset (off) | — |
| Corner radius | 6 px | — | — |

The header is the load-bearing element and it does four jobs at once: it names the node, it
carries the category identity, it provides the node's most visible boundary against the canvas
(every category reads 5.39–10.26:1 there), and it is the one part of a node whose hover
*brightens*, because it carries dark text.

> **Decision V2 — the node header is a full-strength category fill with dark text, not a muted
> band with light text.**
> The rejected alternative was to composite the category colour at about 30% over `node.body`,
> giving a subdued band that could carry `text.primary` like the rest of the interface. It reads
> better as a single node and fails as a canvas: a 30% band sits between 1.90:1 (`cat.script`) and
> 2.46:1 (`cat.input`) against `canvas.bg`, all of them below the 3:1 floor, so a node has no
> visible boundary of its own, the category is barely perceptible
> at 60% zoom, and the LOD transition at 40% becomes a jarring jump from a muted grey-blue node
> to a saturated blue rectangle. A full-strength header makes the LOD transition *continuous* —
> the category colour's share of the node's area simply grows from about 20% to 100% as you zoom
> out — and it puts dark text on a bright fill, which is the one place in a dark interface where
> hover can brighten without violating Principle 2.

### 7.2 Category colour identity

Because ADR-0013 degrades a node to a plain coloured rectangle below 40% zoom, **colour is the
only thing left carrying identity at that scale.** Category colours are therefore chosen for
mutual separation and for contrast against the canvas, not for prettiness.

| Category | Token | Hex | L\* | vs `canvas.bg` (LOD, floor 3) | `text.inverse` on it | Hover (+14% white) | Hover ratio |
|---|---|---|---|---|---|---|---|
| Input & constants | `cat.input` | `#E8C45A` | 80.4 | 10.26 | 10.55 | `#EBCC71` | 11.34 |
| Logic | `cat.logic` | `#B6C455` | 76.1 | 9.06 | 9.31 | `#C0CC6D` | 10.23 |
| Display & preview | `cat.display` | `#71C862` | 73.4 | 8.34 | 8.57 | `#85D078` | 9.54 |
| Geometry · surface & solid | `cat.solid` | `#33B992` | 67.6 | 6.99 | 7.18 | `#50C3A1` | 8.17 |
| Geometry · curve | `cat.curve` | `#4CBCD4` | 71.0 | 7.77 | 7.99 | `#65C5DA` | 8.92 |
| Geometry · point & vector | `cat.point` | `#5AA2EA` | 64.9 | 6.41 | 6.58 | `#71AFED` | 7.66 |
| Script & code | `cat.script` | `#7789EA` | 59.7 | **5.39** | **5.54** | `#8A9AED` | 6.70 |
| Lists | `cat.list` | `#E489C4` | 68.3 | 7.13 | 7.33 | `#E89ACC` | 8.39 |
| Math | `cat.math` | `#DE7B50` | 61.9 | 5.80 | 5.96 | `#E38D68` | 6.99 |
| Custom & uncategorised | `cat.custom` | `#9AA3B2` | 66.7 | 6.79 | 6.98 | `#A8B0BD` | 8.12 |

The lowest LOD figure is `cat.script` at 5.39:1 — comfortably above the 3:1 floor, with the margin
spent deliberately so that a `Script` node stays visible against the canvas even when the display
is badly calibrated.

**Category colours are also separated in lightness, not only in hue.** Adjacent hues differ by at
least **2.77 L\*** (`cat.logic` → `cat.display`), so the set does not collapse into a single band
in greyscale, in a screenshot posted to a forum, or under protanopia. That separation is a real
constraint: ten mutually distinguishable hues inside a 60–81 L\* band is close to the limit of
what is possible, which is why there are ten categories and not fifteen.

**No task in Spark requires telling two categories apart by colour alone.** Three further signals
back it up: a category glyph in the header at ≥73% zoom, a screen-space tooltip naming the node on
hover at *any* zoom including LOD, and the library tree, which is the authoritative statement of
what category a node belongs to.

**Category colours are never used as strokes, and semantic colours are never used as fills**
(Principle 4). That is what stops `cat.display` green from being read as a success state and
`cat.math` orange from being read as a warning, even though they occupy neighbouring hues.

### 7.3 How depth degrades with zoom

A blurred shadow stops reading as depth once its blur radius falls below about 4 device pixels;
below that it is not a shadow, it is a smear that costs fill rate. And a label rendered below
about 8 px is not small text, it is texture. So each cue is dropped at the zoom where it stops
paying for itself, and something cheaper takes over its job.

| Zoom | Depth | Text | Ports | Boundary | What carries identity |
|---|---|---|---|---|---|
| **≥100%** | Full E2: shadow, highlight, lip | header 12 px, ports 11 px, port types 10 px, result strip | shaped, 5–7 px, rank glyphs | 1 px `border.control` | Header colour + title + glyph |
| **82–100%** | Highlight half dropped (6 px blur → under 5 px device) | all | all | 1 px | as above |
| **73–82%** | Shadow dropped entirely; lip retained | **port types dropped** (10 px × 0.82 = 8.2 px); names and the result strip stay | shapes → plain discs | 1 px | Header colour + title |
| **67–73%** | Lip dropped; E0 flat | port labels dropped (11 px × 0.73 = 8.03 px) | 4 px discs | 1 px | Header colour + title |
| **40–67%** | E0 flat | **all text dropped** (12 px × 0.67 = 8.04 px); body fill begins lerping toward the category colour at 60% | 2 px screen-space dots | 1 px | Header colour, growing |
| **<40%** (ADR-0013 LOD) | E0 flat | none | none; wires terminate at the node edge | none — the fill is ≥5.39:1 on its own | **Category colour alone** |

Three ordering details matter more than they look.

**A port's type goes one step before its name.** The name is 11 px and survives to 73%; the type
beside it is 10 px and survives to 82%, by the same eight-pixel arithmetic. Losing the type first
is also the right order for what a user is doing at each zoom: below 82% they are finding a node,
and at 100% they are wiring one.

**Body text is dropped at the same zoom the body fill starts to brighten, and not one step later.**
Between 67% and 40% the body lerps from `node.body` towards its category colour so that the LOD
transition is a fade rather than a jump. Brightening a surface under light text is forbidden by
Principle 2 — so the text goes first. The rule survives because the ordering was chosen to make it
survive.

**Header text outlives body text**, because the header brightens under *dark* text, where
brightening only helps. It is dropped at 67% for size, not for contrast.

> **Decision V3 — labels are dropped, not clamped to a minimum screen size.**
> The obvious alternative is to stop scaling text at 8 px so a zoomed-out graph keeps its names.
> It was rejected because unscaled labels on scaled nodes overlap each other within one zoom step
> of the clamp, and an unreadable overlapping label field destroys the spatial reading that is
> the entire reason for zooming out. Identity at low zoom is recovered by the hover tooltip, which
> is drawn in screen space and works at every zoom including LOD.

### 7.4 Node states on the canvas

| State | Header | Body | Outline | Glyph | Survives LOD? |
|---|---|---|---|---|---|
| Rest | category colour | `node.body` | 1 px `border.control` | category glyph | fill only |
| Hover | +14% white | L−1 | 1 px `border.control`, lip → `lip.hover` | — | screen-space 1 px `accent` outline |
| Selected | + 2 px `accent` underline | L−1 | **2 px `accent` ring** (5.30:1) | — | **yes**, ring at screen width |
| Anchor of a multi-selection | as selected | as selected | as selected + 4 corner ticks | — | yes |
| Focused (keyboard) | — | — | focus sandwich outside the ring | — | yes |
| Warning | `⚠` right-aligned | unchanged | **2 px `state.warning` ring** (6.92:1) | `⚠` | yes |
| Error | `✕` right-aligned | unchanged | **2 px `state.error` ring** (5.69:1) | `✕` | yes |
| Evaluating | small progress glyph | unchanged | 2 px `accent` stroke travelling the outline, 900 ms linear | — | yes, as a static ring |
| Frozen | **desaturated to the equal-luminance grey** | unchanged | 1 px `border.control`, solid | `⏸` | yes, grey fill |
| Preview off | unchanged | unchanged | 1 px `border.control`, **dashed** | eye-off | yes, dashed |
| Not evaluated | desaturated grey | L−2 | 1 px `border.control`, **dashed** | `○` | yes, grey fill |

**Error and warning rings are drawn around the node's outer edge, against the canvas — never on
the header.** An amber warning ring on a gold `cat.input` header would be invisible; against
`canvas.bg` it reads 8.41:1. This is Principle 4 doing concrete work.

**State strokes are drawn at screen width, not scaled.** A 2 px error ring stays 2 px at 15% zoom,
which is what makes an error findable in a zoomed-out graph. It is the only element in the canvas
that refuses to scale, and it refuses because "where is the broken node?" is the question a user
zooms out to answer.

### 7.5 Wires

A wire has to stay visible crossing two very different backgrounds within a single span: the near-
black `canvas.bg` and, further along, a node header at L\* 80.

A single mid-grey stroke chosen to "work against both" cannot be made to work. The window of
relative luminance that clears 3:1 against `canvas.bg` *and* 3:1 against `cat.input` is
**0.0256 wide — about 3.6 units of L\***, and its midpoint is `#6A6A6A`. That grey reads 3.19:1
against the canvas and 3.21:1 against a gold header, with no margin on either side — and then
**2.63:1 against `node.body`, 1.81:1 against `cat.math` and 1.68:1 against `cat.script`**, so it
disappears the moment the wire crosses an ordinary node instead of a bright one. There is no
single stroke colour that solves this.

Spark uses the cartographer's answer: **casing and core.**

| Layer | Width at 100% | Colour | Minimum screen width |
|---|---|---|---|
| Casing | 3.75 px | `wire.casing` `#0E1116` | 2 px |
| Core | 1.75 px | `wire.core` `#C6CDDA` | 1 px |

Exactly one of the two always has the contrast, and the guarantee is checkable:

| Behind the wire | Core reads | Casing reads | Best (floor 3.0) |
|---|---|---|---|
| `canvas.bg` `#171B21` | 10.81 | 1.09 | **10.81** (core) |
| `canvas.group` `#1E222A` | 9.97 | 1.18 | **9.97** (core) |
| `node.body` `#262B33` | 8.90 | 1.32 | **8.90** (core) |
| `cat.input` `#E8C45A` | 1.05 | 11.23 | **11.23** (casing) |
| `cat.logic` `#B6C455` | 1.19 | 9.92 | **9.92** (casing) |
| `cat.display` `#71C862` | 1.29 | 9.13 | **9.13** (casing) |
| `cat.curve` `#4CBCD4` | 1.39 | 8.51 | **8.51** (casing) |
| `cat.list` `#E489C4` | 1.51 | 7.81 | **7.81** (casing) |
| `cat.solid` `#33B992` | 1.54 | 7.65 | **7.65** (casing) |
| `cat.custom` `#9AA3B2` | 1.59 | 7.43 | **7.43** (casing) |
| `cat.point` `#5AA2EA` | 1.68 | 7.01 | **7.01** (casing) |
| `cat.math` `#DE7B50` | 1.86 | 6.35 | **6.35** (casing) |
| `cat.script` `#7789EA` | 2.00 | 5.89 | **5.89** (casing) |

Core against casing is 11.83:1, so the pair reads as a defined line rather than a soft band, and
**the casing is retained at every zoom including LOD** — it is the only thing keeping a wire
legible where it crosses a node, and at LOD every node is a bright rectangle. What LOD drops is
the Bézier subdivision count and the hover and selection affordances, not the casing.

**Type-compatibility feedback during a drag** (Decision V1) recolours the *core* only, leaving the
casing intact so the visibility guarantee still holds:

| Outcome | Core | vs `canvas.bg` | vs `node.body` | Cursor glyph |
|---|---|---|---|---|
| Accepted | `state.success` `#5FD39A` | 9.27 | 7.63 | `✓` |
| Accepted with a lossy conversion | `state.warning` `#F0A63C` | 8.41 | 6.92 | `≈` |
| Refused | `state.error` `#FF7B82` | 6.91 | 5.69 | `✕` |

At rest a wire is neutral. Spark does **not** colour wires by data type: with the type variety an
AEC library produces, per-type wire colour becomes a second, larger palette competing with the
category palette for the same visual channel, and the type of a wire is available on hover and in
the watch panel.

### 7.6 Ports

Port geometry encodes the declared rank of the port, which is the concept
[`concepts.lacing`](lacing.md) §2.2 says everything else depends on. Making it visible on the
canvas means a user can see *why* a node replicated without opening anything.

| Shape | Size | Meaning |
|---|---|---|
| Filled disc | 5 px | Declared rank 0 — wants a single value |
| Disc with one concentric ring | 6 px | Declared rank 1 — wants a list |
| Disc with two concentric rings | 7 px | Declared rank ≥ 2 |
| Rounded square | 5 px | `[KeepStructure]` — unbounded rank, takes the value as given |
| Disc with a flat chord on its outer side | 5 px | `[NoReplication]` — will not fan the node out |

`port.rest` is `#8A93A2` (4.59:1 on `node.body`, 5.57:1 on `canvas.bg`) for an unconnected port and
`port.connected` is `#C6CDDA` — the same value as `wire.core`, so a wire visually terminates in its
port rather than stopping next to it.

The **hit target is 14 × 14 px regardless of the drawn size**, and it does not shrink below 10 px
of screen space as you zoom out. Ports are the smallest thing anyone has to aim at in the product.

**Beside the port name, in `text.muted` at 10 px, is the type the port wants.** `centre  Point3d`.
`radius  number`. `sweepAngle  degrees`. Without it a port is a word and not an instruction: a user
looking at `Circle.ByCentreRadius` for the first time has no way to learn from the node that
`centre` wants a point, and the two places that would have told them — the library entry's
signature and the colour of a wire being dragged at it — are both somewhere other than where the
question is asked. `text.muted` reads 6.28:1 on `node.body`, and it is the token this design
language already reserves for units and counts, which is the register a type annotation belongs to.

Three rules keep it from becoming noise.

- **The name wins the row.** The type is a step smaller and a step dimmer, and it is dropped a
  level of detail before the name ([§7.3](#73-how-depth-degrades-with-zoom)).
- **A name that already says the type does not say it twice.** An output called `circle` returning
  a `Circle` is drawn as `circle`, and so is a `curves` input taking a list of `Curve`. The
  suppression is on the words, so `points` returning `Point3d` still shows the type — the kernel
  type really is `Point3d`, and somebody hunting the library for one is better off knowing.
- **Listness is not repeated.** Whether a port wants one value or many is the ring in the table
  above, and the type names the element. A node that said both would be spending width to say the
  same thing twice.

The type is drawn **only when the row has room for it** — a node is sized before any text has been
measured, so what a row cannot fit it does without, rather than overlapping the port name opposite.

### 7.6.1 The creation box

Double-clicking empty canvas opens a search box at the pointer
([`concepts.finding-nodes`](finding-nodes.md)). It is `surface.float` with a 1 px
`border.control` frame and a soft drop shadow — the elevation [§4](#4-elevation-and-depth)
reserves for menus, popups and autocomplete — because it floats over a canvas that sits only
1.2:1 away from it and needs an edge to be readable at all.

**It is a real Avalonia control, and it is the first one.** Every node on this canvas is drawn
rather than instantiated (ADR-0013), and the exception the decision always allowed for is the thing
currently being interacted with: one control, in screen space, over the drawing. The in-place node
editor belongs in the same layer when it arrives.

Two behaviours are part of the design rather than of the implementation. The box is **clamped
inside the canvas**, so a double-click near an edge does not open something half unreachable. And
the node lands at the point that was **double-clicked**, not at the pointer's position when Enter
was pressed — the wheel can pan and zoom while the box is open, and a node arriving somewhere other
than where it was asked for is the kind of small betrayal that makes a gesture feel unreliable.

### 7.6.2 The result strip

Under every node that produced something is a strip showing what it produced: `surface.sunken`
with the node's own 1 px `border.control` outline and a 4 px radius, one 15 px row when closed and
one row per value when open, `text.muted` for the headline and `text.secondary` for the values.

**The headline carries the rank, and that is the requirement rather than a detail.** `8 items ·
rank 1`. A hundred points at rank 1 and a hundred at rank 2 draw identically in the viewport and
lace completely differently ([`concepts.lacing`](lacing.md) §2.2), so rank is the fact a graph
author most often has wrong and it goes in the line that cannot be closed. A single value is
headlined by its type instead — `Circle`, `number` — which is the same question answered for a
thing there is only one of.

Open, it adds the first six values and then says how many it did not show. **Six and a count, not
six and silence**: a preview that stops without saying so makes a list of a hundred read as a list
of six.

**An open strip widens to fit its values, up to 2.5× the node's width.** The values are the reason
it was opened, and a coordinate triple cut to the node's width shows two of the three numbers
somebody wanted. Past the cap — and past whatever the width estimate got wrong — the text is
ellipsised **to the measured box**, never to a character count: a fixed count against a variable
width fits forty-four narrow characters and overflows on forty-four digits, which is exactly how
the first version of this strip wrote a list of decimals out through its own border.

Three consequences are chosen rather than inherited.

- **It sits outside the node's box.** The node's own bounds are what a marquee selects and what
  the spatial index culls on, and growing them by whatever the last run produced would make
  selection depend on evaluation results.
- **A node drawn later covers an open preview.** Nodes win over previews, which is the right
  priority when they collide — the graph is the document and the preview is a readout of it.
  Moving the node apart is the remedy, and the alternative, previews floating over nodes, would
  hide the thing being worked on to show a readout of it.
- **Open or closed is not saved.** It is what you are looking at rather than what you have made,
  so it is not in the `.spark` file and undo does not touch it, exactly like pan and zoom.

### 7.7 Frozen, preview off, and not evaluated

Three states that all mean "this node is not currently contributing", and all three are easy to
implement badly by fading the node until it cannot be read.

**Frozen** desaturates the header to the grey of *identical relative luminance* — `cat.point`
`#5AA2EA` becomes `#9E9E9E`, `cat.math` `#DE7B50` becomes `#969696`. Because the substitution is
luminance-preserving, header text contrast is unchanged to within a hundredth: 6.58 → 6.62 and
5.96 → 6.00. The state is carried by the loss of *hue*, which costs no contrast at all, plus a
`⏸` glyph and a solid outline.

**Preview off** — the node computes, but its geometry is not drawn in the viewport — changes no
colour whatever. The preview toggle moves from E2 raised to E1 inset, its glyph becomes eye-off,
and the node outline becomes dashed. Depth is exactly the right vocabulary for this, because
nothing about it concerns legibility.

**Not evaluated** is the grey state that [`concepts.lacing`](lacing.md) §2.5 describes: when an
upstream node errors, downstream nodes are greyed as *not evaluated* rather than flooded with
errors of their own. It uses the frozen desaturation, plus a dashed outline, plus a `○` glyph, plus
a body at L−2 — which *raises* text contrast to 15.12:1. A user must still be able to read a node
that did not run, because reading it is how they work out what should have run.

**Evaluating** is the only animated state: a 2 px `accent` stroke travelling around the node
outline on a 900 ms linear loop, plus a progress glyph in the header. Under reduced motion the
stroke becomes a static, complete `accent` ring and the glyph becomes `…`. The information is
retained; only the movement is removed. Below 55% zoom the travelling stroke is replaced by the
static ring for every user, because animating hundreds of outlines is not affordable inside the
60 fps target ADR-0013 sets.

### 7.8 Groups and notes

A **group** is `canvas.group` `#1E222A` with a 1 px `border.control` frame and a title bar in
`surface.base` carrying `text.secondary` at 9.97:1. The fill sits at 1.08:1 against the canvas on
purpose — a group is a region, not an object, and its frame and title are what identify it.

A **note** is E2 on `surface.raised` with `text.primary` at 12.30:1, and it obeys the ordinary
state ladder. Notes are read, so they are held to text rules, not to canvas rules.

---

## 8. The 3D viewport

### 8.1 Ground and grid

| Token | Hex | Notes |
|---|---|---|
| `viewport.top` | `#1B1F26` | Top of a vertical gradient |
| `viewport.bottom` | `#14171D` | Bottom of the gradient |
| `grid.minor` | `#2A313C` | 1 model unit; 1.26:1 against the ground |
| `grid.major` | `#3A414D` | every 10 units; 1.60:1 against the ground |

The background is a gradient rather than a flat fill because a flat dark field gives the eye no
horizon and makes a rotating model feel like it is floating in nothing; the gradient supplies a
weak up-direction for free. It is dithered with the same 1.5% noise as the shadows, for the same
banding reason.

It is deliberately not black. Shaded geometry needs somewhere to put its own shadow side, and on a
black ground the dark faces of a mesh merge with the void and the silhouette reads as a hole.

The grid ratios are low on purpose and are covered by the exemption in
[§4.2](#42-what-the-floors-do-not-cover): a grid is a scene element that must be legible when you
look for it and invisible when you do not. Both grids fade with distance.

### 8.2 Axes

| Axis | Token | Hex | vs `grid.major` (floor 3) | vs ground |
|---|---|---|---|---|
| X | `axis.x` | `#DE7176` | 3.29 | 5.29 |
| Y | `axis.y` | `#6DC576` | 4.84 | 7.79 |
| Z | `axis.z` | `#6699E0` | 3.52 | 5.66 |

Red, green and blue for X, Y and Z, despite the collision with `state.error`, `state.success` and
`state.info`. This is the one place where an outside convention beats internal consistency: every
CAD and DCC tool an AEC user has ever opened uses this mapping, and inventing a fourth colour
scheme for axes would cost more than the collision does. Three things contain it:

- axis colours appear **only** on the ground-plane axis lines and in the corner orientation
  triad, and nowhere else in the product;
- the triad is always **labelled X, Y and Z in text**, so the mapping never depends on hue;
- the values are distinct hexes from the semantic tokens, so a reviewer who finds `#FF7B82` in
  viewport code knows immediately that something is wrong.

### 8.3 Geometry and selection

| Token | Hex | Role | vs ground |
|---|---|---|---|
| `geometry.surface` | `#AEB7C6` | Default shaded surface, at full lighting | 8.17 |
| `geometry.edge` | `#E6EAF1` | Edges, isoparms, curve strokes | 13.69 |
| `geometry.casing` | `#0E1116` | The dark casing under every overlay stroke | — |

Geometry is a light neutral rather than white, so that the specular range has somewhere to go and a
lit face is distinguishable from a blown-out one.

**Selection uses the same casing-and-core trick as wires**, and for exactly the same reason: a
selection outline has to be visible against both the near-black ground and the light geometry it
surrounds.

| Layer | Width (screen space) | Colour | Contrast |
|---|---|---|---|
| Casing | 3 px | `geometry.casing` `#0E1116` | 9.35:1 against `geometry.surface` |
| Core | 1.5 px | `accent` `#A98BFF` | 7.04:1 against the casing, 6.15:1 against the ground |

The outline is drawn in **screen space**, so a selected object one metre away and one a hundred
metres away have the same 1.5 px outline and both are findable. Selected surfaces additionally
receive a 15% `accent` lighting tint, but the tint is never the only signal — the outline is
authoritative.

Points are drawn as 5 px screen-space discs in `geometry.edge` with a 1 px `geometry.casing` ring,
because an unringed light dot vanishes the moment it lands on a light surface.

### 8.4 Ghosted geometry: the one declared exception

When geometry preview is isolated to the selection, everything else is ghosted to
`geometry.ghost` `#616A79`. That is **3.02:1 against the viewport ground** — perceivable — but only
**2.70:1 against `geometry.surface`**, which is below 3.

This is the single pairing in the document that sits under a floor, and it is unavoidable rather
than sloppy: for ghosted geometry to be 3:1 below active geometry it would need a relative
luminance of at most 0.113, and for it to be 3:1 above the ground it would need at least 0.140.
No colour satisfies both. So the requirement is discharged differently:

- ghosted geometry is drawn **edges-only, unshaded**, while active geometry is shaded. The
  distinction is a *rendering mode*, not a contrast ratio, and it is absolute;
- ghosting is never the authoritative statement of what is previewed. The node's preview toggle
  in the graph is ([§7.7](#77-frozen-preview-off-and-not-evaluated));
- **selected geometry is never ghosted**, so nothing a user is acting on is ever in this state.

### 8.5 Viewport overlays

Everything drawn *on top of* the scene is UI and is fully inside the contrast rules: the view-cube,
the navigation bar, the units readout, the frame-rate counter, dimension text, and warning banners.
They sit on E3 floating surfaces with their own fills — `text.primary` on `viewport.top` reads
15.12:1 — and never as bare text over unknown geometry.

---

## 9. Typography

### 9.1 Families

| Role | Stack | Why |
|---|---|---|
| UI | `Inter`, `Segoe UI Variable Text`, `Segoe UI`, `Noto Sans`, `sans-serif` | Inter has a tall x-height and open apertures, which is what keeps 11 px port labels readable; it disambiguates `1 l I` and `0 O`, which matters when the label is a parameter name and the value is a number; and it has real tabular figures. It is OFL-licensed and ships with the app, so a Linux build (ADR-0001) renders identically. Segoe UI is the fallback because it is on every Windows machine and has similar metrics, so a missing Inter degrades without reflowing the layout. |
| Monospace | `JetBrains Mono`, `Cascadia Mono`, `Consolas`, `monospace` | For the code block node and the script editor. JetBrains Mono has a slashed zero, visually distinct bracket families, and a taller-than-usual x-height at small sizes, which is what a nine-hour editing session needs. Cascadia ships with Windows Terminal and Consolas with Windows itself, so the fallback chain never reaches a default the user has not seen before. |

**Numeric fields use tabular figures** (`tnum`) — watch panels, sliders, coordinate readouts,
property grids. Proportional digits make a value that updates during a drag appear to jitter
sideways, which is both distracting and a genuine reading error when comparing a column of numbers.

### 9.2 Scale

At 100% UI scale. Every size scales with the OS display scale; none of them scale with canvas zoom
except node text, which is dropped rather than scaled below 8 px ([§7.3](#73-how-depth-degrades-with-zoom)).

| Step | Size | Weight | Line height | Used for |
|---|---|---|---|---|
| Display | 28 px | 600 | 1.2 | Empty states, the welcome screen |
| Title | 22 px | 600 | 1.25 | Dialog titles |
| Heading | 18 px | 600 | 1.3 | Section headings in the inspector and in help |
| Subheading | 15 px | 500 | 1.35 | Group labels, the inspector's node name |
| **Body** | **13 px** | **400** | **1.45** | Everything by default |
| Dense | 12 px | 400 / 500 | 1.35 | Node header titles (500), table cells, tree rows |
| Caption | 11 px | 500 | 1.3 | Port labels, badges, units, all-caps micro-labels (+0.04 em tracking) |
| Code | 13 px | 400 | 1.55 | The code block node and the script editor |

**11 px is the floor.** Nothing in Spark is set smaller, at any scale, in any state.

### 9.3 Weight on a dark ground

Light text on a dark ground optically **blooms**: the glyph appears heavier than the same weight
does on a light ground, and thin strokes smear. So Spark uses one weight step lighter than a light
theme would at every level:

- body is 400 and **never 300** — a 300 weight at 13 px on `#23272F` loses stroke definition on
  any panel that is not perfectly calibrated;
- emphasis is 500 where a light theme would reach for 600;
- 700 is not in the scale at all. Where a light theme would use 700, Spark uses 600 at the next
  size up.

Weight is never the only signal for a state, because a weight change is worth roughly nothing at
11 px.

---

## 10. Motion

### 10.1 Tokens

| Token | Duration | Used for |
|---|---|---|
| `motion.instant` | 0 ms | Anything following the pointer directly: drags, pans, resizes, sliders |
| `motion.fast` | 90 ms | Hover in, ripple-free press feedback, port growth |
| `motion.base` | 140 ms | Hover out, selection, checkbox and toggle transitions |
| `motion.slow` | 220 ms | Popups, menus, tooltips appearing; elevation changes |
| `motion.deliberate` | 320 ms | Panel open and close, dock rearrangement, dialog entry |
| `motion.ambient` | 900 ms | The evaluating loop; the only repeating animation in the product |

| Easing | Curve | Used for |
|---|---|---|
| `ease.standard` | `cubic-bezier(0.2, 0, 0, 1)` | Anything entering or changing state |
| `ease.exit` | `cubic-bezier(0.4, 0, 1, 1)` | Anything leaving |
| `ease.linear` | `linear` | Indeterminate progress only |

Hover in is `motion.fast`; hover out is `motion.base`. Faster in, slower out, so a pointer crossing
a dense list does not strobe.

### 10.2 On the canvas

Motion is a frame-budget question here, not a taste question (ADR-0013 targets 2,000 nodes at
60 fps).

- Node hover is a `motion.fast` colour interpolation and nothing else — no elevation animation,
  no scale, no ripple.
- All per-node transitions are **switched off entirely** when more than 400 nodes are visible or
  the zoom is below 60%. Below those thresholds state changes are instantaneous, which is also
  what a user zoomed out to survey a graph actually wants.
- Pan and zoom are `motion.instant` and follow the input device exactly. There is no smoothing,
  no inertia and no easing on a viewport transform; nothing makes a canvas feel worse than a
  canvas that keeps moving after you stop.
- The one exception is *zoom to fit* and *frame selection*, which are `motion.deliberate` with
  `ease.standard`, because the user did not choose the destination and needs to see where they
  went.

### 10.3 Reduced motion

Spark honours the operating system's reduced-motion setting, and there is no in-app override that
can turn it back on.

When it is set:

- every duration above collapses to `motion.instant` **except** `motion.deliberate` view
  transitions, which collapse to a 60 ms crossfade so that a jump-cut does not itself become
  disorienting;
- the evaluating loop becomes a static `accent` ring plus a `…` glyph — the state is still
  reported, only the movement is removed;
- indeterminate progress bars become a static striped fill that advances only when the underlying
  state actually changes.

**No information is ever carried by motion alone**, so reduced motion never removes a signal. That
is a design constraint on every future feature, not a post-processing step.

---

## 11. What this style is not allowed to do

Concrete grounds for rejecting a change in review. Each one is a real failure mode of the style
Spark has chosen, not a hypothetical.

1. **Text distinguished only by shadow.** If removing every shadow from a screenshot makes any
   text, icon or label harder to read, the screen is wrong.
2. **Hover that lowers contrast.** Brightening a dark surface under light text, or darkening a
   bright fill under dark text. The direction is stated in [§5.1](#51-the-hover-rule-and-why-it-inverts-the-usual-move)
   and it has no exceptions.
3. **Focus shown only by a glow.** Or only by a shadow, only by an elevation change, only by a
   colour change, or only on hover. The focus sandwich in [§6](#6-keyboard-focus) is the
   indicator; anything else is in addition to it.
4. **Semantic state shown only by colour.** No error without a glyph and words, no warning without
   a glyph and words, no connection outcome without a cursor glyph.
5. **A control boundary below 3:1 where the boundary is what identifies the control.** Depth so
   soft that an icon button becomes a smudge is a bug, and the fix is `border.control`, not a
   larger blur.
6. **Opacity used to signal state.** Not for disabled, not for not-evaluated, not for frozen, not
   for inactive tabs. Opacity reduces contrast by construction; the ladder, the desaturation and
   the flattening in [§5.5](#55-disabled) and [§7.7](#77-frozen-preview-off-and-not-evaluated) do
   the same jobs without it.
7. **An accent or semantic tint behind text.** Selection uses a spine, a ring and a weight change
   ([§5.4](#54-selected)). Tints are for surfaces nobody reads.
8. **A fifth elevation level, or a shadow with a blur radius outside {5, 6, 12, 16, 28}.** The set is
   fixed so the canvas can cache shadow sprites; a one-off blur is a frame-budget regression
   dressed as a design tweak.
9. **A category colour used as a stroke, or a semantic colour used as a fill.** Principle 4 is what
   keeps a green node from being read as a passing node.
10. **A pure black (`#000000`) shadow, or a surface below L\* 6 carrying elevation.** Both produce
    the punched-hole artefact described in [§2.8](#28-the-hard-part-neumorphic-depth-on-a-dark-ground).
11. **Text below 11 px, or a weight of 300 anywhere.**
12. **Information carried by motion alone**, which disappears entirely under reduced motion.

---

## 12. Worked examples

### 12.1 A `Number Slider` node, all the way down the zoom range

A `Number Slider` is an `Input` node, so its category colour is `cat.input` `#E8C45A`.

```text
 100%   ┌─────────────────────────────┐    header  #E8C45A, text.inverse @ 10.55:1
        │ ◈ Number Slider          ◉  │    body    #262B33, text.secondary @ 8.90:1
        ├─────────────────────────────┤    outline 1 px #7C8595 @ 3.82:1 / 4.64:1 on canvas
        │  ▁▁▁▁▁●▁▁▁▁▁▁▁      12.500  │    track   E1 inset on #1A1E24
        │  min 0.0        max 100.0   │    value   text.primary @ 13.02:1, tabular figures
        └─────────────────────────────┘    E2: shadow +3/+4 blur 12, highlight −2/−2 blur 6,
                                            lip #3E4654

  90%   same, minus the highlight half of the shadow pair

  78%   same, minus the shadow; the lip and the 1 px outline carry the depth

  70%   port labels gone (11 px × 0.70 = 7.7 px, below the 8 px floor);
        ports are plain 4 px discs; the title survives

  55%   all text gone (12 px × 0.55 = 6.6 px); the body has lerped 60% of the way
        from #262B33 towards #E8C45A; ports are 2 px screen-space dots

  30%   ██  a #E8C45A rounded rectangle, 10.26:1 against the canvas.
            No text, no ports, no shadow, no outline.
            Hovering it still shows a screen-space tooltip reading "Number Slider".
```

Now hover it at 100%: the header goes to `#EBCC71` and its dark title rises from 10.55:1 to
11.34:1; the body goes to `#20242B` and the value text rises from 13.02:1 to 14.24:1; the lip goes
from `#3E4654` to `#8674D6`; the shadow grows from +3/+4 blur 12 to +4/+6 blur 16. Four things
changed and **every readable thing got easier to read.**

### 12.2 Hovering a row in the node library

The library is a tree on `surface.base`. Each row has a node name in `text.secondary` and a
category in `text.muted`.

| | Fill | Name (`text.secondary`) | Category (`text.muted`) |
|---|---|---|---|
| Rest | `#23272F` | 9.36:1 | 6.61:1 |
| **Hover** | `#1D2128` | **10.10:1** | **7.13:1** |
| **Selected** | `#181C22` + 3 px `accent` spine | **10.69:1**, weight 500 | **7.55:1** |
| **Selected and hovered** | `#181C22` + spine | 10.69:1 | 7.55:1 |
| **Focused** | rest fill + focus sandwich | 9.36:1 | 6.61:1 |

The row gets visibly *more* substantial as you interact with it and the text gets easier to read at
every step. Notice that focus changes no fill at all — it adds four pixels of hard-edged ring
outside the row, so a row can be focused and unselected, or selected and unfocused, and you can
always tell which.

### 12.3 A node that errors while it is selected

`Circle.ByCenterRadius` is a `Geometry · curve` node (`cat.curve` `#4CBCD4`). It has been given a
string where it wanted a number, so the engine raises `SPK1040` ([`concepts.lacing`](lacing.md) §7)
and the node produces no output. The user has it selected.

Three signals stack without any of them cancelling another:

- **Selected** — a 2 px `accent` `#A98BFF` ring, 5.30:1 against the node body; body at
  `#20242B`, so the title reads 14.24:1 rather than 13.02:1.
- **Error** — a 2 px `state.error` `#FF7B82` ring drawn *outside* the selection ring, 6.91:1
  against the canvas; a `✕` glyph in the header; the diagnostic code and message in `state.error`
  in the node's footer at 5.69:1.
- **Downstream** — every node fed by this one takes the *not evaluated* state: header desaturated
  to its equal-luminance grey, body at `#1B1F26`, dashed outline, `○` glyph. Their titles read
  **15.12:1**, up from 13.02:1, because a user needs to read the nodes that did not run in order
  to work out what should have.

Zoom out to 25% and the two rings are still there, drawn at screen width, on a `#4CBCD4` rectangle.
The graph's one broken node is findable from a view where nothing has any text on it.

---

## 13. Decisions taken in this document

| # | Decision | Section |
|---|---|---|
| **V1** | The type-compatibility wire colours **are** the semantic tokens, reused in a different role, not a parallel green/amber/red ramp. | [§2.5](#25-borders-accent-and-semantics), [§7.5](#75-wires) |
| **V2** | The node header is a **full-strength category fill with dark text**, not a muted band with light text. | [§7.1](#71-node-anatomy) |
| **V3** | Canvas labels are **dropped** below 8 px rendered size, never clamped to a screen-space minimum. | [§7.3](#73-how-depth-degrades-with-zoom) |
| **V4** | Hover, press and selection move a surface **away from its text**, which on Spark's dark surfaces means **darker**, inverting the usual dark-theme move. | [§5.1](#51-the-hover-rule-and-why-it-inverts-the-usual-move) |
| **V5** | Every node on the canvas carries a **1 px `border.control` outline**. This is the largest concession the neumorphic aesthetic makes, and it is made because `node.body` sits at 1.21:1 against `canvas.bg` and a node's extent is what you aim at. | [§4.6](#46-non-text-boundaries) |
| **V6** | Most of the neumorphic **lit-side budget is spent on a 1 px lip** rather than a wide highlight blur — because a broad light-on-dark highlight reads as a glow, and because a lip is the one bright element that is never behind text, which is what makes rule V4 workable. | [§2.8](#28-the-hard-part-neumorphic-depth-on-a-dark-ground) |
| **V7** | Focus is a **4 px hard sandwich** — dark, light, dark — drawn outside the control, never a glow, never an elevation change, and never suppressed by hover. | [§6](#6-keyboard-focus) |
| **V8** | Disabled is signalled by the **removal of depth**, not the removal of contrast; `text.disabled` clears 4.5:1 everywhere. | [§5.5](#55-disabled) |
| **V9** | Wires and 3D selection outlines use a **casing-and-core pair** so that one of the two strokes always clears 3:1 against whatever is behind it. | [§7.5](#75-wires), [§8.3](#83-geometry-and-selection) |
| **V10** | **X/Y/Z stay red/green/blue** despite colliding with the semantic hues, contained by strict role scoping and always-present text labels. | [§8.2](#82-axes) |
| **V11** | The application mark is **drawn from the SVG's own path strings** rather than imported as a bitmap, and the window icon is rendered from that drawing at startup — so there is no `.ico` in the tree and no second copy of the artwork to fall out of date. | [§15.1](#151-the-mark) |
| **V12** | The splash's indeterminate bar is the **one sanctioned exception to Principle 6**. Everything else on the splash is still, and the status line is set once rather than updated, because the UI thread is blocked and an update could never paint. | [§15.2](#152-the-splash) |
| **V11** | Spark applies the **4.5:1 body floor to large text as well**, and does not take WCAG's disabled-text exemption. | [§4.1](#41-the-floors) |
| **V12** | Ghosted geometry is the **one declared exception** to a floor (2.70:1 against active geometry, which no colour can satisfy alongside 3:1 against the ground); it is discharged by a rendering-mode difference instead. | [§8.4](#84-ghosted-geometry-the-one-declared-exception) |

---

## 14. Verifying this document

Every ratio here comes from the sRGB relative-luminance formula in WCAG 2.2, with results
truncated downward so a printed figure is never a rounded-up pass. Two checks belong in CI from
M2, alongside the canvas benchmark:

1. **Palette check.** Parse the token table, recompute every pairing in
   [§4](#4-contrast-rules-with-numbers), and fail the build if any figure differs from the one
   printed here or falls below its floor. The tables are the fixture, exactly as
   [`concepts.lacing`](lacing.md)'s case table is the fixture for the engine.
2. **Monotonicity check.** For every element in [§5.2](#52-the-full-state-matrix), assert that the
   contrast of every text token in the hover, pressed, selected and focused states is greater than
   or equal to its contrast at rest. This is Principle 2 as an assertion, and it is the one that
   catches the failure the style is prone to.

If a future change makes a number in this document wrong, the correct response is to change the
palette until the number is right again — not to edit the number.

---

## 15. The application mark, and the splash

**Numbered last on purpose.** Sections 1 to 14 are cited by number from source comments and from
other documents — `§7.3` and `§2.7` both appear in code — so a new section inserted where it
logically belongs would silently invalidate every one of those references. The mark arrived after
the numbering did; stable references are worth more than tidy ordering.

### 15.1 The mark

`assets/spark-icon.svg` is the master. The application does not load it: `Theming/SparkLogo.axaml`
draws the same geometry, and **carries the SVG's path strings verbatim** — Avalonia's geometry
syntax *is* SVG path syntax, so there is one source of truth, no export step and no bitmap to fall
out of date. `SparkLogoTests` fails if the two stop agreeing.

It is built from tokens on this page rather than from colours chosen for it:

| Element | Token | Hex |
|---|---|---|
| Tile, top of gradient | `surface.base` | `#23272F` |
| Tile, bottom of gradient | `bg.void` | `#12151A` |
| Tile rim light | `depth.hi.float` | `#414A59` |
| Spark, top of gradient | `accent.press` | `#D4C4FF` |
| Spark, middle | `accent` | `#A98BFF` |
| Spark, bottom | `lip.hover` | `#8674D6` |
| Ring | `accent` at 28% | `#A98BFF` |
| Control points | `accent.hover` | `#C0A8FF` |

Two ideas, layered so they **degrade in the declared order** — the discipline
[§7.3](#73-how-depth-degrades-with-zoom) applies to canvas cues, applied here:

- **The spark is the silhouette**, and at 16 px it is the entire mark.
- **The ring carrying two control points** is the parametric curve every Spark document is made
  of. It shows through the spark's concave waists; below about 32 px it stops being readable as a
  ring and becomes a halo, which is an acceptable thing for it to become.

**The waist was measured, not chosen.** Rendered with a control offset of 46 the mark reads as a
gem rather than a spark; at 14 it is elegant and loses too much mass at 16 px. It is 22, picked by
rendering all four at 512, 48, 32 and 16 px and looking at them side by side.

**There is no `.ico` in the repository.** The window icon is rendered from the same drawing at
startup, so the taskbar cannot disagree with the splash. `SparkLogo.CreateWindowIcon` returns null
rather than throwing when there is no render target — an application that refuses to start because
it could not draw its own icon has its priorities wrong.

### 15.2 The splash

Shown while the shell is built, because that takes over a second and most of it is invisible work:
importing the node library reflects over an assembly, and the seeded graph is evaluated before
anything can be drawn.

- **Rectangular, not rounded.** A rounded splash needs window transparency, and where the platform
  declines to grant it Avalonia paints the window background into the corners — so the rounded
  version degrades to black notches exactly where you cannot see it happening.
- **One status line, set once and never updated.** The shell is built by a single blocking call on
  the UI thread, so a status changed just before it would be assigned and never painted. A label
  describing the whole wait is honest; a sequence of steps nobody sees is not.
- **Never shown for `--screenshot` or `--canvas-benchmark`**, for two different reasons: it would
  be a second window in the capture, and a second window compositing against the thing being
  measured.

**The indeterminate bar is this document's one sanctioned exception to Principle 6**
("nothing moves that the user did not move"). The user did move it — they launched the
application — and a splash showing no sign of life is indistinguishable from one that has hung.
It uses `ease.linear`, which [§10.1](#101-tokens) already reserves for indeterminate progress.
Nothing else on the splash moves.

---

## See also

- [`concepts.lacing`](lacing.md) — replication and lacing; the source of the port rank shapes in
  [§7.6](#76-ports), the error and warning severities in [§5.6](#56-error), and the
  *not evaluated* state in [§7.7](#77-frozen-preview-off-and-not-evaluated).
- [ADR-0013](../../adr/0013-immediate-mode-node-canvas.md) — the immediate-mode canvas and the 40%
  level-of-detail threshold that [§7.3](#73-how-depth-degrades-with-zoom) is written against.
- [ADR-0001](../../adr/0001-avalonia-not-wpf.md) — Avalonia, Skia, and why the 3D viewport in
  [§8](#8-the-3d-viewport) is ours to draw.
