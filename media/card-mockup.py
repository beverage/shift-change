#!/usr/bin/env python3
"""Render the Steam description cards as a local HTML mockup.

    ./media/card-mockup.py            # writes media/_card-mockup.html

CONTENT LIVES IN `CARDS` BELOW. Everything else is presentation. Edit the copy
there, re-run, reload the browser — the point of this file is that iterating on
what a card SAYS costs nothing, so the wording gets argued with before anyone
starts compositing pixels.

WHY HTML AND NOT IMAGEMAGICK: `card-triptych.sh` composites photographs, where
every constant is measured off one capture. These cards are text, and text
reflows — a paragraph that grows by a line must not need remeasuring. CSS does
that for free. When the copy settles, a headless screenshot of each `.card`
node at 640 px produces the shipping PNGs.

TYPOGRAPHY, which is the whole reason for the file:

  * Titles use RimWordFont, embedded below as a data URI so the mockup renders
    the same on any machine. That face is UPPERCASE-ONLY, so it can never carry
    body text.
  * Body copy uses a humanist sans stand-in. The game's real UI font ships
    inside RimWorld's Unity asset bundles, not as a loose file, so it cannot be
    referenced here — anywhere the true font matters, use a SCREENSHOT of the
    game instead of typesetting the words ourselves. That is what the `shot`
    field is for, and it is why the UI cards look native for free.

Three text registers, following Vanilla Events Expanded's cards:

  flavor  italic, dimmed  — text the GAME shows, quoted verbatim
  body    plain           — our explanation of the mechanic
  note    small, grey     — asides

Pure stdlib, no dependencies.
"""

import base64
import html
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
FONT = os.environ.get("FONT", os.path.expanduser("~/Downloads/RimWordFont.ttf"))
OUT = os.path.join(HERE, "_card-mockup.html")

#: Steam renders a description image at up to ~640 px before scaling it down.
#: Vanilla Events Expanded ships 640, VFE Empire 600. Match the wider one.
CARD_W = 640

#: Display width for every in-entry shot, so they share a left AND a right edge
#: instead of only a left one. 521 is the work-types dialog's interior width, so
#: that one lands at exactly 1:1; the owner dialog is natively 696 and comes down
#: to 75%, which on a 2x capture is still half again over native. Alignment is
#: worth that much more than the last of the sharpness — a column of shots that
#: each end somewhere different reads as an accident.
SHOT_W = 521

# --------------------------------------------------------------------------
# CONTENT
# --------------------------------------------------------------------------
# `kind` picks the title-bar accent: it groups cards by what they are for, the
# way VEE colours a threat bar differently from a psychic one.
#
# Markup inside copy: **bold** for a term or a default, `code` for a literal
# in-game label, !!warning!! for the one thing on a card that bites.
#
# `lede` IS USUALLY EMPTY, AND SHOULD STAY THAT WAY. A title followed by a
# one-line restatement of the title is the single most recognisable tell of
# machine-written copy — "Four optional controls, on every stand", "Deliberately
# narrow, so it never fights you". Every card had one; read down the page they
# were audibly the same sentence five times. A lede now has to carry something
# the title does not (Card_WorkTypes says where the dialog is reached from) or
# it does not exist. Flavour is allowed once, not as a template.

#: The page sequence, mirroring steam-description.bbcode. Keep the two in
#: step: the whole value of the mockup is that what you scroll here is what the
#: store page will be, and an order that only approximates it hides exactly the
#: problems worth catching (a card that lands under the wrong banner, two dense
#: cards back to back with nothing to break them).
ORDER = [
    # Carries the two animations and a blurb each. They are GIFs, so they stay
    # [img] tags in the description and cannot be baked into a card — this
    # banner has no card under it here by design.
    "Banner_WhatItDoes",
    "Banner_HowItWorks",
    "Card_Gizmos", "Card_Controls", "Card_Removing",
    # The exclusions stayed a plain bullet list in the description: four short
    # negatives are a list, and setting them in a picture would cost a card to
    # say less than the text does. The rules went the same way and further —
    # they are now questions in the FAQ, because a page that opens as a call to
    # action should not land on minutiae before it has been installed.
    "Banner_Limits",
    "Banner_Compatibility",
    "Banner_FAQ",
    "Card_FAQ",
    "Banner_Source",
]

#: id -> filename. The ids are PascalCase because they read as names in code;
#: media/ is lowercase-with-dashes and was long before these arrived, so the
#: files that land beside card-chef.png and card-tooltip.png match those.
#:
#: Written out rather than slugified. A camel-splitter would produce nine of
#: these correctly and then have an opinion about "FAQ", and the tenth entry is
#: the one that matters: Card_Controls CANNOT be card-controls.png, because that
#: name is taken by the 2000x900 gallery contact sheet this card is cut from.
#: Same subject, different artifact. Naming it for the card's own title keeps
#: both, and keeps card-crops.sh reading from a file no export can overwrite.
ASSET = {
    "Banner_WhatItDoes": "banner-what-it-does",
    "Banner_HowItWorks": "banner-how-it-works",
    "Banner_Limits": "banner-limits",
    "Banner_Compatibility": "banner-compatibility",
    "Banner_FAQ": "banner-faq",
    "Banner_Source": "banner-source",
    "Card_Gizmos": "card-gizmos",
    "Card_Controls": "card-stand-controls",
    "Card_Removing": "card-removing",
    "Card_FAQ": "card-faq",
}

#: Cards deliberately absent from this mod's page, and so never rasterised into
#: cards/ by --export. Two different reasons, both of which want the card kept
#: renderable so it can be argued with:
#:
#:   Card_Modes       describes stand behaviour that does not exist yet. cards/
#:                    is a public repo served over raw githubusercontent, and a
#:                    player who opened that URL could not tell it from shipped.
#:   Card_PairsWith   is for Apparel Painter's store page, not this one. It is
#:                    drafted here only because the generator is here; when that
#:                    mod's page is built the card moves with it, and it must not
#:                    land in this mod's cards/ in the meantime.
#:
#: Listed rather than merely absent so a card that falls out of ORDER by accident
#: is still reported.
DRAFT = {"Card_Modes", "Card_PairsWith"}

BANNERS = [
    ("Banner_WhatItDoes", "What it does"),
    ("Banner_HowItWorks", "How it works"),
    ("Banner_Limits", "What it will not do"),
    ("Banner_Compatibility", "Apparel and other mods"),
    # Save safety was its own section until the two questions it answered — can
    # I add this to a running colony, is it safe to remove — turned out to be
    # questions, and questions have a home now.
    ("Banner_FAQ", "Questions"),
    ("Banner_Source", "Source, testing and credits"),
    # Not on this page. Drafted here because the generator is here; it belongs
    # on Apparel Painter's, pointing back at this mod. See DRAFT below.
    ("Banner_PairsWith", "Pairs with"),
]

#: HOMELESS COPY, kept so it cannot be lost. The gendered-clothing warning was
#: the one thing in the retired Dressing for recreation card that nothing else
#: on the page says, and it documents real shipped behaviour (a shared stand
#: serves anyone who can wear ANYTHING on it, so a man takes the prestige robe
#: and leaves the ladies hat). It does not belong in the opening, which is a
#: call to action, and the section it lived in is gone. Pending a decision:
#: either a footnote under Stand controls' owner entry, or an eighth question
#: in the FAQ. The README and About.xml still carry their own version, so the
#: warning is not currently absent from everything, only from the store page.
PARKED = {
    "gendered-clothing":
        "A stand serves anyone who can wear *something* on it. Fine for a lab "
        "coat, a trap for a gown: put a prestige robe, a ladies hat and a "
        "formal shirt on a shared stand, and a man will take the robe and "
        "leave the hat. Owner lists exist for this. **All / Men / Women** tabs "
        "and an **Assign all** button make it the women's stand in two clicks.",
}

CARDS = [
    {
        "id": "Card_Gizmos",
        "kind": "controls",
        "title": "The buttons on a stand",
        "lede": "",
        # Three stand buttons, then the one that is not on the stand at all.
        "gizmos": ["cards/parts/giz-mode.png", "cards/parts/giz-owners.png",
                   "cards/parts/giz-removing.png", "cards/parts/giz-changeback.png"],
        "entries": [
            {
                "body": "Select a stand and the first two appear. The switch names "
                        "what that stand is currently dressing for, so its face "
                        "changes as you set it, and beside another outfit-stand mod "
                        "it also decides whose owner control you see.",
            },
            {
                "body": "**Allow removing items** is vanilla's own button, held off "
                        "here while a stand is in service. **Change back** is the odd "
                        "one out: it sits on the colonist rather than the stand, and "
                        "only while they are wearing a uniform.",
            },
        ],
    },
    {
        "id": "Card_Controls",
        "kind": "controls",
        "title": "Stand controls",
        "lede": "",
        "entries": [
            {
                "shot": "cards/parts/sec-type.png",
                "lead": "The switch",
                "body": "the top of the stand's own dialog, and the only control that "
                        "must be set for anything to happen at all. It reads back **what "
                        "the game thinks the room is**, which can be surprising, for "
                        "example, !!a crib can turn a hospital into a barracks!!. Set it "
                        "by hand for a room that does two jobs, or for a decorative "
                        "stand you do not want used.",
            },
            {
                "shot": "cards/parts/sec-flags.png",
                "lead": "Change the whole outfit",
                "body": "**off by default.** The difference between a lab coat worn over "
                        "ordinary clothes and a sauna robe worn instead of them. Ticked, "
                        "the colonist wears only what the stand holds and loses whatever "
                        "their own clothes were providing, !!warmth included!!.",
            },
            {
                "lead": "Keep contents out of trade",
                "body": "**on by default.** Traders will otherwise buy anything on an "
                        "outfit stand: the uniform, and the owner's own clothes parked "
                        "there while they are on shift. Untick it for a stand you keep "
                        "as a shop shelf.",
            },
            {
                "shot": "cards/parts/sec-activities.png",
                "lead": "What sets it off",
                "body": "every work type in your game, plus recreation. Left on "
                        "**Automatic** the stand follows the room and this list only "
                        "reports what that resolved to. Tick them yourself for rooms that "
                        "don't fit what the game thinks they are, like multi-purpose rooms.  " 
                        "Recreation, and work types are **mutually exclusive**,  with one "
                        "outfit serving one purpose.",
            },
            {
                "shot": "cards/parts/dlg-owners.png",
                "lead": "Set owners",
                "body": "restricts a stand to the colonists you list: one for a personal "
                        "kit, several to serve a group and nobody outside it. Left "
                        "unassigned it is **shared**, like a bed, so a kitchen needs one "
                        "stand per cook working *at once*, not one per cook.",
            },
        ],
    },
    {
        "id": "Card_Removing",
        "kind": "controls",
        "title": "Allow removing items",
        "lede": "",
        "pair": ["cards/parts/giz-removing.png", "cards/parts/tip-text.png"],
        "entries": [
            {
                "body": "This vanilla toggle is **held off while the stand is in "
                        "service**. Turning it on exposes the stand's contents to every "
                        "colonist's outfit optimizer, which may simply take the uniform "
                        "and wear it as everyday clothes. It never covered trade.",
            },
            {
                "body": "To take a garment back, use the **eject button** in the stand's "
                        "Contents tab.",
            },
        ],
    },
    {
        "id": "Card_FAQ",
        "kind": "rules",
        "title": "",
        "lede": "",
        "entries": [
            {"q": "Will it interrupt an order I gave?",
             "body": "No. Only automatic work triggers a change, so a direct order is "
                     "never delayed by a wardrobe trip."},
            {"q": "What happens in an emergency?",
             "body": "Nobody changes into a uniform while the map is under threat, "
                     "and firefighting or a rescue is never held up. A colonist "
                     "already in one still changes back, which on a full-change "
                     "stand is how they get back to their armour."},
            {"q": "Can another colonist take the uniform?",
             "body": "Not from a stand with owners; that one serves only the colonists "
                     "you list. Nobody takes clothes out of a stand somebody else is "
                     "using, either."},
            # Was a line in the opening, where it answered a question nobody had
            # been given yet. It is the first thing a player worries about once
            # they understand what the mod does, which makes it a question.
            {"q": "What happens to their own clothes?",
             "body": "They wait in the stand while the shift lasts, and come back "
                     "exactly as they were, force-worn markers included."},
            {"q": "Do they change just walking through the room?",
             "body": "No. Only doing the room's work does it."},
            {"q": "Do they eat in uniform?",
             "body": "No, a meal break gets them changed first, wherever the food is "
                     "stored. Food already in hand they simply eat."},
            {"q": "What if every stand was busy when they started?",
             "body": "If one frees up while they are working in its room out of uniform, "
                     "they step over and change, unless they are mid-treatment on a "
                     "patient."},
            # NOT "games room": one full of pool tables scores as a rec room and
            # does self-enable, so the old wording asked about a case that never
            # happens, and its own answer then listed the rooms that do not.
            {"q": "Why didn't my throne room switch on by itself?",
             "body": "Only a room the game scores as a rec room does that. A throne "
                     "room, a dining room with a chess table in it, and a pool usually "
                     "do not; set those by hand, once."},
            {"q": "How do I get someone out of a uniform right now?",
             "body": "A **Change back** button appears on any colonist wearing one."},
            {"q": "Can I add it to an existing save?",
             "body": "Yes, freely. Nothing needs rebuilding."},
            {"q": "Is it safe to remove?",
             "body": "Yes. Stands revert to ordinary furniture, and a colonist mid-shift "
                     "keeps the uniform, with their own clothes waiting in the stand. "
                     "**Clear forced apparel** on the Assign tab un-forces it."},
        ],
    },
    {
        # FOR APPAREL PAINTER'S PAGE, not this one. The pairing is not "also by
        # the same author": Shift Change is what produces the wall of stands that
        # makes per-item painting worth having, so the card names the reader's
        # situation rather than the relationship between two mods.
        "id": "Card_PairsWith",
        "kind": "pairing",
        "title": "Uniform walls",
        "lede": "",
        "entries": [
            {
                "body": "**Shift Change** dresses a colonist for the room they walk "
                        "into: scrubs in the hospital, whites in the kitchen, a robe for "
                        "the sauna. In practice that means a wall of stands, and every "
                        "garment on it arrives in whatever colour its "
                        "material happened to be.",
            },
            {
                "body": "Painting them one at a time is what makes that wall readable "
                        "across a room, and it is the case this mod was built for. The "
                        "alternative is dressing a colonist, pausing, and hand-editing "
                        "each garment in a character editor.",
            },
        ],
    },
    {
        "id": "Card_Modes",
        "kind": "modes",
        "title": "Where a stand reaches",
        "lede": "PROPOSED: nothing below this line ships yet.",
        "entries": [
            {"lead": "The room",
             "body": "the stand's own room, read from its role. Shipped since v1.0.0, and "
                     "still the default. Select a stand and the game outlines the room "
                     "for you."},
            {"lead": "The room next door",
             "body": "a linked door lets a changing room serve the workshop through it. "
                     "One scrub room can face both a ward and an operating theatre."},
            {"lead": "A fenced enclosure",
             "body": "for outdoor grounds. Fence a pool yard and the fence is the "
                     "boundary; leave it unfenced and the stand does nothing, because an "
                     "open map edge is not an enclosure."},
            {"lead": "An area you paint",
             "body": "the escape hatch for spaces the other three cannot describe. Under "
                     "consideration, and the least likely: an area can silently be the "
                     "whole map."},
        ],
    },
]

# --------------------------------------------------------------------------
# PRESENTATION
# --------------------------------------------------------------------------

#: Sampled from RimWorld's own UI chrome, which is what the screenshots bring
#: with them — the synthetic parts of a card have to sit in the same palette or
#: the seam shows.
CSS = """
:root {
  --page:   #17191c;
  --bg:     #262b30;
  --panel:  #1f242a;
  --inset:  #191d21;
  --edge:   #3d4650;
  --rule:   #2f363d;
  --text:   #d6d3cc;
  --dim:    #8b939b;
  --title:  #f5f0e7;
  --accent: #d9b04a;
  --warn:   #d97a6c;
}
* { box-sizing: border-box; }
body {
  margin: 0; padding: 40px 20px; background: var(--page);
  font-family: "Helvetica Neue", Helvetica, Arial, "Liberation Sans", sans-serif;
  display: flex; flex-direction: column; align-items: center; gap: 26px;
}
.card, .banner { width: %(W)dpx; }

/* ---- section banner: 640x50, the reusable strip ---- */
.banner {
  height: 50px; display: flex; align-items: center;
  padding: 0 16px; border: 1px solid var(--edge);
  background: linear-gradient(180deg, #6d5c3f 0%%, #55482f 100%%);
}
.banner .t {
  font-family: RimWord, sans-serif; font-size: 20px; letter-spacing: 1.5px;
  color: var(--title);
}

/* ---- card ---- */
.card { background: var(--panel); border: 1px solid var(--edge); }
.card > .bar {
  display: flex; align-items: baseline; gap: 10px; padding: 10px 16px;
  border-bottom: 1px solid var(--edge);
  background: linear-gradient(180deg, #2c333b 0%%, #232930 100%%);
}
.card > .bar .t {
  font-family: RimWord, sans-serif; font-size: 19px; letter-spacing: 1.4px;
  color: var(--title);
}
.card > .bar .k { width: 3px; align-self: stretch; }
.card[data-kind="controls"]   .k { background: #6f8fb0; }
.card[data-kind="rules"]      .k { background: #7fa37c; }
.card[data-kind="recreation"] .k { background: #b08fc4; }
.card[data-kind="limits"]     .k { background: #c48a6a; }
.card[data-kind="modes"]      .k { background: #b0a06f; }
.card[data-kind="pairing"]    .k { background: #8fa9c4; }

.lede { padding: 12px 16px 0; color: var(--dim); font-size: 13.5px; font-style: italic; }
.card[data-kind="modes"] .lede { color: var(--accent); font-style: normal; font-size: 12.5px; letter-spacing: .4px; }

/* The gizmo set, laid in one row. Four 137 px buttons and three 12 px gaps
   come to 584 inside a 608 content width, so they fit at native size — no
   scaling, which matters because these carry the game's own label text. */
.gizmos { display: flex; gap: 12px; padding: 14px 16px 2px; }
.gizmos img { display: block; border: 1px solid var(--edge); }

/* A button beside the tooltip it raises, which is how they meet on screen and
   which costs a third of the height of stacking them. The icon keeps its native
   size; the panel takes whatever is left, so the pair always fills the width
   whatever the two are measured at. */
.pair { display: flex; gap: 12px; align-items: flex-start; padding: 14px 16px 2px; }
.pair > img { display: block; flex: 0 0 auto; border: 1px solid var(--edge); }
.pair > .wide { flex: 1 1 auto; min-width: 0; }
.pair > .wide img { display: block; width: 100%%; border: 1px solid var(--edge); }

.shot { padding: 14px 16px 2px; }
.shot img { width: 100%%; display: block; border: 1px solid var(--rule); }

/* A shot belonging to ONE entry. Capped at its own native width so a 521 px
   row strip is never upscaled into mush — these are game UI captures, and
   RimWorld's text stops surviving interpolation almost immediately.

   THIS BORDER IS THE ONLY ONE. Panel slices are cropped inside the game's own
   frame (see card-crops.sh), so nothing draws an edge but us, and every slice
   from one dialog is the same 521 px wide — they stack in exact alignment
   rather than stepping in and out. */
.eshot { margin: 2px 0 10px; }
.eshot img { width: 100%%; display: block; border: 1px solid var(--edge); }

.entries { padding: 14px 16px 18px; display: flex; flex-direction: column; gap: 13px; }
.entry { font-size: 14px; line-height: 1.5; color: var(--text); }
.entry + .entry { border-top: 1px solid var(--rule); padding-top: 13px; }

/* the in-game label, quoted verbatim */
.flavor {
  display: block; margin-bottom: 7px; padding: 6px 9px;
  background: var(--inset); border: 1px solid var(--rule);
  font-style: italic; font-size: 13px; color: var(--dim);
}
/* A bold run-in, closed with a full stop. NOT an em dash: the separator is
   generated, so one character here becomes twenty on the page, and the em
   dash is the specific tell a commenter already called out. */
.lead { color: var(--title); font-weight: 600; }
.lead::after { content: ". "; }

/* A question runs in ahead of its answer. Separate from .lead because a lead
   gets a full stop generated after it and a question already ends in its own
   punctuation. */
.q { color: var(--title); font-weight: 600; }
b { color: var(--title); font-weight: 600; }
em { color: var(--text); }
.entry.warn { border-left: 3px solid var(--warn); padding-left: 11px; margin-left: -3px; }
.entry.warn .lead { color: var(--warn); }
mark { background: none; color: var(--warn); font-weight: 600; }

/* --page only: the description's own prose and its animations, so the whole
   store page can be read in one scroll instead of cards in one window and
   copy in another. */
.prose { width: %(W)dpx; font-size: 14px; line-height: 1.55; color: var(--text); }
.prose a { color: #7fb0d8; }
.prose b { color: var(--title); }
.prose ul { margin: 0; padding-left: 20px; }
.prose li { margin-bottom: 9px; }
.gif { width: %(W)dpx; }
.gif img { width: 100%%; display: block; border: 1px solid var(--edge); }

/* mockup furniture, never part of a shipped card */
.label {
  width: %(W)dpx; color: #5d666e; font-size: 11px; letter-spacing: 1px;
  text-transform: uppercase; margin-bottom: -18px;
}
"""

#: --export only, appended after CSS. One node to a page with no page chrome,
#: and TRANSPARENT behind it, so the trim after the screenshot lands exactly on
#: the card's own outer edge instead of guessing at a height.
EXPORT_CSS = """
body { margin: 0; padding: 0; background: transparent; display: block; }
.card, .banner { width: %(W)dpx; }
"""


def markup(s):
    """**bold**, *italic*, `code`, !!warning!! -> HTML, everything else escaped."""
    s = html.escape(s)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s)
    s = re.sub(r"\*(.+?)\*", r"<em>\1</em>", s)
    s = re.sub(r"`(.+?)`", r"<code>\1</code>", s)
    s = re.sub(r"!!(.+?)!!", r"<mark>\1</mark>", s)
    return s


def render_entry(e):
    cls = "entry warn" if e.get("warn") else "entry"
    out = [f'<div class="{cls}">']
    if e.get("flavor"):
        out.append(f'<span class="flavor">{html.escape(e["flavor"])}</span>')
    # The shot sits ABOVE its paragraph: the reader looks at the control, then
    # reads what it does. Reversed, the prose describes something not yet seen.
    if e.get("shot"):
        w = min(e.get("shotw", SHOT_W), CARD_W - 32)
        out.append(f'<div class="eshot" style="max-width:{w}px">'
                   f'<img src="{e["shot"]}" alt=""></div>')
    body = e["body"]
    if e.get("q"):
        out.append(f'<span class="q">{markup(e["q"])}</span> ')
    if e.get("lead"):
        out.append(f'<span class="lead">{markup(e["lead"])}</span>')
        # The lead closes with a full stop, so the body opens a new sentence.
        # Done here rather than in the copy: the separator is a presentation
        # choice, and it should be possible to change it without re-editing
        # every paragraph in the file.
        body = re.sub(r"[a-z]", lambda m: m.group().upper(), body, count=1)
    out.append(markup(body))
    out.append("</div>")
    return "".join(out)


def render_card(c):
    out = ([f'<div class="label">{c["id"]}.png</div>'] if SHOW_LABELS else []) + [
           f'<div class="card" data-kind="{c["kind"]}">']
    # An empty title means no title bar. A card that sits directly under its own
    # section banner would otherwise print the same word twice, once in the
    # banner's display face and again in the card's.
    if c["title"]:
        out.append('<div class="bar"><div class="k"></div>'
                   f'<span class="t">{html.escape(c["title"])}</span></div>')
    if c.get("lede"):
        out.append(f'<div class="lede">{html.escape(c["lede"])}</div>')
    if c.get("pair"):
        icon, wide = c["pair"]
        out.append(f'<div class="pair"><img src="{icon}" alt="">'
                   f'<div class="wide"><img src="{wide}" alt=""></div></div>')
    if c.get("gizmos"):
        row = "".join(f'<img src="{g}" alt="">' for g in c["gizmos"])
        out.append(f'<div class="gizmos">{row}</div>')
    if c.get("shot"):
        out.append(f'<div class="shot"><img src="{c["shot"]}" alt=""></div>')
    out.append('<div class="entries">')
    out.extend(render_entry(e) for e in c["entries"])
    out.append("</div></div>")
    return "\n".join(out)


#: --page suppresses the "Card_Foo.png" captions; on a page read they are
#: scaffolding, and the point of that mode is to see what a player sees.
SHOW_LABELS = True

BB = re.compile(r"\[(/?)(b|i)\]|\[url=([^\]]+)\]|\[/url\]")


def bbcode(s):
    """The tags the description actually uses: b, i, url, list/* and img."""
    if "[list]" in s:
        items = [i.strip() for i in s.split("[*]")[1:]]
        items = [i.replace("[/list]", "").strip() for i in items]
        return "<ul>" + "".join(f"<li>{bbcode(i)}</li>" for i in items) + "</ul>"
    out, pos = [], 0
    for m in BB.finditer(s):
        out.append(html.escape(s[pos:m.start()]))
        pos = m.end()
        if m.group(2):
            out.append(f'</{m.group(2)}>' if m.group(1) else f'<{m.group(2)}>')
        elif m.group(3):
            out.append(f'<a href="{html.escape(m.group(3))}">')
        else:
            out.append("</a>")
    out.append(html.escape(s[pos:]))
    return "".join(out).replace("\n", " ")


def render_page(bare_after=None):
    """Render the real description, substituting live cards for their [img]s.

    Reading steam-description.bbcode rather than keeping a second copy of
    the prose here: two copies of the same paragraphs drift, and the one that
    ships is the bbcode. Cards are not exported to PNG yet, so where the page
    points at media/cards/Card_Foo.png this splices in the rendered card.

    bare_after: from that banner onward, draw section headers but not their
    cards — for showing where finished work stops without pretending the rest
    is missing.
    """
    src = os.path.join(HERE, "steam-description.bbcode")
    cards = {c["id"]: c for c in CARDS}
    banners = dict(BANNERS)
    out, bare = [], False
    for block in re.split(r"\n\s*\n", open(src).read().strip()):
        block = block.strip()
        m = re.fullmatch(r"\[img\](.+?)\[/img\]", block)
        if not m:
            out.append(f'<div class="prose">{bbcode(block)}</div>')
            continue
        url = m.group(1)
        name = (url.rsplit("/", 1)[-1].rsplit(".", 1)[0]
                if "/media/cards/" in url else None)
        if name in banners:
            out.append(render_banner(name, banners[name]))
            bare = bare or (bare_after and name == bare_after)
        elif name in cards:
            if not bare:
                out.append(render_card(cards[name]))
        else:
            out.append(f'<div class="gif"><img src="{html.escape(url)}" alt=""></div>')
    return out


def render_banner(fid, text):
    # The section title alone. The mod's name used to sit at the right of every
    # bar; six of them down one page is six repetitions of a word already in the
    # page title, the mod title and the URL.
    label = f'<div class="label">{fid}.png</div>' if SHOW_LABELS else ""
    return (label +
            f'<div class="banner"><span class="t">{html.escape(text)}</span></div>')


def main():
    face = ""
    if os.path.exists(FONT):
        b64 = base64.b64encode(open(FONT, "rb").read()).decode("ascii")
        face = ("@font-face{font-family:RimWord;src:url(data:font/ttf;base64,"
                + b64 + ") format('truetype');}")
    else:
        print(f"warning: {FONT} not found — titles fall back to a system face",
              file=sys.stderr)

    # Named ids on the command line render just those, to their own file. The
    # separate filename is not cosmetic: the preview pane caches a snapshot per
    # URL, so re-rendering a section over the full page's filename shows the
    # stale page instead of the new section.
    global SHOW_LABELS
    args = sys.argv[1:]
    page = "--page" in args
    bare_after = next((a.split("=", 1)[1] for a in args
                       if a.startswith("--bare-after=")), None)
    only = [a for a in args if not a.startswith("-")]
    cards = {c["id"]: c for c in CARDS}
    banners = dict(BANNERS)

    missing = [i for i in ORDER if i not in cards and i not in banners]
    if missing:
        print("  ORDER names nothing that exists: " + ", ".join(missing),
              file=sys.stderr)

    if "--export" in args:
        SHOW_LABELS = False
        dest = os.path.join(HERE, "cards", "_export")
        os.makedirs(dest, exist_ok=True)
        # Wipe it. A page left behind by an earlier run is still rasterised by
        # card-export.sh, which is how a card retired from ORDER came back as a
        # PNG after being deleted.
        for stale_page in os.listdir(dest):
            os.remove(os.path.join(dest, stale_page))
        style = "<style>" + face + (CSS % {"W": CARD_W}) \
                + (EXPORT_CSS % {"W": CARD_W}) + "</style>"
        n = 0
        for ident in ORDER:
            if ident in cards:
                node = render_card(cards[ident])
            elif ident in banners:
                node = render_banner(ident, banners[ident])
            else:
                continue
            # Relative image paths have to keep resolving, so each page is
            # written beside the parts it references, not into a temp dir.
            node = node.replace('src="cards/', 'src="../')
            if ident not in ASSET:
                print(f"  {ident} has no ASSET filename — not exported",
                      file=sys.stderr)
                continue
            open(os.path.join(dest, ASSET[ident] + ".html"), "w").write(
                "<!doctype html><meta charset=utf-8>" + style + node)
            n += 1
        print(f"wrote {n} export pages to {dest}")
        print("  now run card-export.sh to rasterise them")
        return 0

    if page:
        SHOW_LABELS = False
        body = render_page(bare_after)
        out = os.path.join(HERE, "_page.html")
    else:
        # Named ids render in the order given, whether or not ORDER contains
        # them. That is what makes a DRAFT card reviewable at all: it is absent
        # from the page by definition, so filtering ORDER would never find it.
        body = []
        for ident in (only or ORDER):
            if ident in cards:
                body.append(render_card(cards[ident]))
            elif ident in banners:
                body.append(render_banner(ident, banners[ident]))
        out = OUT if not only else os.path.join(HERE, "_card-section.html")
    doc = ("<!doctype html><meta charset=utf-8><title>Shift Change — card mockup</title>"
           "<style>" + face + (CSS % {"W": CARD_W}) + "</style>"
           + "\n".join(body))
    open(out, "w").write(doc)
    print(f"wrote {out}")
    print(f"  {len(body)} blocks, {CARD_W}px wide"
          + (" (page)" if page else "")
          + (f", bare after {bare_after}" if bare_after else "")
          + (f", filtered to {', '.join(only)}" if only and not page else ""))

    stale = [c["id"] for c in CARDS if c["id"] not in ORDER and c["id"] not in DRAFT]
    if stale:
        print("  not in ORDER, so not on the page: " + ", ".join(stale),
              file=sys.stderr)

    # An em dash in a COMMENT is nobody's business; one in card copy ends up on
    # the store page, and a Workshop commenter named the punctuation as the tell
    # that a mod was machine-written. The v1.2.3 pass replaced 70 of them by
    # hand across the README, About.xml and the keyed strings; nothing stopped
    # the next one drifting back in, so this does.
    strays = []
    for c in CARDS:
        for field in ("title", "lede"):
            if "—" in c.get(field, ""):
                strays.append(f'{c["id"]}.{field}')
        for i, e in enumerate(c["entries"]):
            for field in ("flavor", "lead", "body"):
                if "—" in e.get(field, ""):
                    strays.append(f'{c["id"]}.entries[{i}].{field}')
    for _, text in BANNERS:
        if "—" in text:
            strays.append(f"banner {text!r}")
    if strays:
        print("  EM DASH in player-facing copy: " + ", ".join(strays), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
