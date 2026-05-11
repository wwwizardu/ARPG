"""
Phase C 신규 빌드 가능 오브젝트 13종의 ComfyUI 생성용 프롬프트 정의.

기준 프롬프트(사용자 제공, Bedroll 예시):
  masterpiece, best quality, game asset, unrolled bedroll, fantasy RPG style,
  rustic fabric texture, soft padded interior, flat laid-out rectangular shape,
  worn canvas surface, stitched patches and folded edges, cozy resting vibe,
  wilderness camp prop, Dragon Quest inspired, vibrant colors, clean render,
  concept art, detailed shading

각 항목은 위 양식을 따라 핵심 묘사만 오브젝트에 맞게 변형.
"""

# 공통 prefix / suffix
PREFIX = "masterpiece, best quality, game asset"
SUFFIX = "Dragon Quest inspired, vibrant colors, clean render, concept art, detailed shading, top-down 3/4 view, isolated on transparent background"
NEGATIVE = "low quality, blurry, jpeg artifacts, watermark, text, logo, multiple objects, busy background, photorealistic, human figure"

# 13종 오브젝트 — Phase C에서 추가된 BuildableItemTable 엔트리
ITEM_PROMPTS = [
    {
        "id": 112,
        "name": "Stockpile",
        "core": (
            "stone stockpile, pile of mining stones, rough quarry rocks heaped together, "
            "gravel and small boulders mixed, weathered grey surface, sturdy storage pile, "
            "village resource depot, rugged texture, mossy edges"
        ),
    },
    {
        "id": 140,
        "name": "ChoppingBlock",
        "core": (
            "wooden chopping block, axe stuck in tree stump, woodcutter station, "
            "fresh wood chips scattered around, exposed tree rings, bark texture, "
            "rustic lumberjack prop, sturdy round base"
        ),
    },
    {
        "id": 141,
        "name": "DryingRack",
        "core": (
            "wooden drying rack, hanging strips of meat and fish, hunter's preservation rack, "
            "lashed wooden poles, sun-dried texture, rope and twine details, "
            "rustic wilderness prop, weathered timber"
        ),
    },
    {
        "id": 142,
        "name": "MiningCart",
        "core": (
            "rusty mining cart on rails, ore-filled minecart, iron wheels, "
            "weathered wooden body with metal bands, pile of raw ore visible inside, "
            "industrial mining prop, gritty rust patina"
        ),
    },
    {
        "id": 150,
        "name": "Hearth",
        "core": (
            "stone hearth fireplace, glowing embers and warm orange flames, "
            "cooking pot hung over fire, cobblestone arched opening, "
            "soot-stained masonry, cozy kitchen prop, gentle smoke wisps"
        ),
    },
    {
        "id": 151,
        "name": "MerchantStall",
        "core": (
            "wooden merchant stall, striped awning canopy, displayed goods on counter, "
            "market vendor table with crates and baskets, hanging coin pouch, "
            "colorful trade banners, lively bazaar prop"
        ),
    },
    {
        "id": 152,
        "name": "TownPost",
        "core": (
            "wooden town post, carved village name sign, directional arrow markers, "
            "weathered standing post stuck in ground, lantern hanging from top, "
            "small notice paper nailed on, welcoming village landmark"
        ),
    },
    {
        "id": 153,
        "name": "InnBed",
        "core": (
            "cozy inn bed, wooden bedframe with patchwork quilt, fluffy pillow, "
            "tavern lodging room prop, soft warm linens, side table with candle, "
            "homely resting vibe, polished wood headboard"
        ),
    },
    {
        "id": 154,
        "name": "SignalBrazier",
        "core": (
            "iron signal brazier on tall wooden pole, burning coals in metal cage, "
            "watchtower fire beacon, glowing embers and rising sparks, "
            "reinforced iron brackets, night-watch guardian prop"
        ),
    },
    {
        "id": 160,
        "name": "Furnace",
        "core": (
            "stone forge furnace, glowing molten interior with intense orange light, "
            "blacksmith's forge, brick chimney with rising smoke, "
            "soot-stained masonry, sturdy iron grate, heat shimmer aura"
        ),
    },
    {
        "id": 161,
        "name": "Anvil",
        "core": (
            "iron blacksmith anvil on heavy wooden block, hammer resting on top, "
            "scorched metal surface with hammer marks, sparks frozen mid-air, "
            "polished horn and base, master craftsman prop"
        ),
    },
    {
        "id": 162,
        "name": "QuenchVat",
        "core": (
            "wooden quench vat barrel, water-filled tub with rippling surface, "
            "blacksmith's cooling station, iron tongs leaning against the rim, "
            "iron-bound oak staves, faint steam rising"
        ),
    },
    {
        "id": 170,
        "name": "Shrine",
        "core": (
            "small stone shrine altar, lit candles and offerings of flowers, "
            "mossy carved stones with faded runes, mystical soft glow, "
            "sacred forest grove prop, weathered religious icon"
        ),
    },
    {
        "id": 181,
        "name": "PalisadeGate",
        "core": (
            "wooden palisade gate, double wooden doors with iron hinges, "
            "two tall log posts framing the entrance, sturdy wood beams across the top, "
            "rustic village gate seen from front 3/4 angle, "
            "weathered timber and iron studs, slightly open showing path through, "
            "cozy fortified entrance"
        ),
    },
    # ===== 스킬북 3종 (SKILLBOOK_DESIGN.md §2.2) — 등급별 ItemTable 행 =====
    {
        "id": 5000,
        "name": "SkillBookCommon",
        "core": (
            "old worn spellbook, weathered brown leather cover with simple iron clasp, "
            "frayed edges and dog-eared yellowed pages peeking out, "
            "plain embossed circular emblem on the front, faded ink stains, "
            "humble apprentice tome, single book lying flat closed, "
            "rustic novice grimoire prop, muted brown and tan tones"
        ),
    },
    {
        "id": 5001,
        "name": "SkillBookRare",
        "core": (
            "fine arcane spellbook, deep blue leather cover with silver filigree trim, "
            "ornate metal corner caps and silver lock buckle, embossed runic sigil glowing softly, "
            "ribbon bookmark hanging from gilded edge pages, polished and well-kept, "
            "scholar's enchanted tome, single book lying flat closed, "
            "magical adept grimoire prop, sapphire blue and silver tones"
        ),
    },
    {
        "id": 5002,
        "name": "SkillBookEpic",
        "core": (
            "legendary mystical tome, rich purple and black leather cover with intricate gold filigree, "
            "large central gemstone inlay glowing with violet arcane light, "
            "ornate gold corner guards and clasp, faintly glowing magic runes etched along the spine, "
            "wisps of purple magical aura floating around the book, gilded page edges, "
            "ancient archmage's grimoire, single book lying flat closed, "
            "epic legendary spellbook prop, deep purple and gold tones"
        ),
    },
    # ===== 스킬 페이지 3종 (SKILL_RUNE_DESIGN.md §7.1) — 등급별 ItemTable 행 =====
    # 스킬북에 장착하는 한 장짜리 페이지 아이템 (책이 아님 — 단일 양피지/스크롤)
    {
        "id": 5100,
        "name": "SkillPageCommon",
        "core": (
            "single old parchment page, weathered yellowed paper with rough torn edges, "
            "simple black ink runes and basic magical sigils written across the surface, "
            "small ink blots and faded handwriting, "
            "humble apprentice spell page, one flat sheet lying open, "
            "rustic novice scroll page prop, muted brown and tan tones, "
            "no book, just a loose page"
        ),
    },
    # 책 페이지 느낌으로 시도하는 변종 (양피지/스크롤이 아니라 한 장의 책 페이지)
    {
        "id": 5100,
        "name": "SkillPageCommonBook",
        "core": (
            "ONE SINGLE THIN SHEET OF PAPER, paper-thin single layer, "
            "razor-thin edges showing only one layer of paper, no visible thickness on any edge, "
            "looks as thin as a real piece of paper, single ply, "
            "rectangular shape with all four edges clean and straight, "
            "smooth flat surface, light cream colored slightly aged, "
            "very subtle faint shadow underneath - barely a hint, just enough to suggest one thin sheet not a stack, "
            "viewed from directly above looking straight down, top-down flat view, "
            "printed text laid out in two neat columns of elegant black calligraphy, "
            "decorative red drop-cap initial letter at the top of the first column, "
            "small simple magical sigil illustration centered between the columns, "
            "tiny page number at the bottom corner, thin ink border framing the text area, "
            "humble apprentice's spell page, "
            "(((NOT a stack of pages))), (((NOT layered papers))), (((NOT thick))), "
            "absolutely no book visible, no binding, no other pages underneath, no scroll, no parchment, "
            "not rolled, not curled, completely flat, single layer paper only"
        ),
    },
    {
        "id": 5101,
        "name": "SkillPageRare",
        "core": (
            "single arcane parchment page, fine cream colored paper with neatly trimmed edges, "
            "intricate silver ink runes and glowing blue magical sigils, "
            "ornate decorative border drawn around the text, faint blue magical shimmer along the runes, "
            "scholar's enchanted spell page, one flat sheet lying open, "
            "magical adept scroll page prop, sapphire blue and silver tones, "
            "no book, just a loose page"
        ),
    },
    {
        "id": 5102,
        "name": "SkillPageEpic",
        "core": (
            "single legendary mystical page, rich aged parchment with gilded edges, "
            "elaborate gold ink runes and powerful glowing violet magical sigils, "
            "intricate ornamental border with gold filigree, wisps of purple magical aura floating around the page, "
            "central glowing arcane symbol radiating violet light, "
            "ancient archmage's spell page, one flat sheet lying open, "
            "epic legendary scroll page prop, deep purple and gold tones, "
            "no book, just a loose page"
        ),
    },
    {
        # 모든 빌딩이 건설 중일 때 공통으로 사용하는 placeholder 스프라이트.
        # 시각적으로 "공사 중" 임을 즉시 인지할 수 있어야 함 (목재 골조 + 비계 + 작업 도구).
        "id": 999,
        "name": "UnderConstruction",
        "core": (
            "wooden construction scaffolding, half-built timber framework, "
            "exposed wooden beams forming a partial structure, "
            "stacked planks and wooden boards leaning against the frame, "
            "construction tools nearby — hammer, saw, sawhorse, "
            "rope tied around posts, sawdust on the ground, "
            "work-in-progress village building site, rustic wooden skeleton, "
            "no walls yet, framework only, daylight"
        ),
    },
]


def make_positive_prompt(item: dict) -> str:
    """item['core']를 prefix/suffix와 결합해 완성된 양성 프롬프트 반환."""
    return f"{PREFIX}, {item['core']}, fantasy RPG style, {SUFFIX}"


def make_negative_prompt() -> str:
    return NEGATIVE
