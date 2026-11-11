# Арт гардероба и непрерывного подъёма

Все перечисленные рисунки созданы встроенным image_gen. Сохранены в проекте; оригиналы остались в каталоге generated_images. Шесть отдельных пробных фонов отклонены в пользу одной непрерывной панорамы: это не используемые игровые локации.

## Используемые файлы

- `Assets/Resources/WorldZones/AscentPanorama.png` — единый рисунок снизу вверх: луг, редкие деревья, облака, высокое небо, атмосфера, космос.
- `Assets/Resources/Menu/Accessories/Flower.png`
- `Assets/Resources/Menu/Accessories/Glasses.png`
- `Assets/Resources/Menu/Accessories/Tufts.png`
- `Assets/Resources/Menu/Accessories/Star.png`
- `Assets/Resources/Menu/WardrobeArt.png` — используются только прежние лапка и часы; изображения шарфов больше не выбираются и не отображаются.

Одежда использует фактуру исходного котика и отдельный цветовой слой в шейдере, без прямоугольных накладок. Исходный PNG котика не изменён.

## Следы прыжка

Две прежние ленты сохранены с прежними идентификаторами и ценами. Добавлены
«Звёздная пыль» (190 монет), «Цветочный вихрь» (220) и «Светлячки» (280).
Это разные небольшие рисунки с тёплым чернильным контуром: неровные звёздочки,
поворачивающиеся лепестки и светлые крылышки с золотым брюшком.

Новые следы рисуются кодом в `JumpTrailArt`, одинаково в гардеробе и в игре.
`JumpTrailVisuals` использует повторно пул из 12 рисунков, не расходует случайные
числа генератора уровня и не добавляет коллайдеры. Рисунки появляются только
при подъёме, исчезают за 0,5–0,64 секунды, очищаются при переносе через границу,
смерти, возвращении в меню или отключении компонента; пауза останавливает затухание.

## Финальный запрос панорамы

Use case: illustration-story. Asset type: ONE SINGLE CONTINUOUS VERTICAL SCROLLING PANORAMA background for a 2D upward-jumping game, tall aspect ratio 1:3, about 1280 wide by 3840 tall. This is NOT a collage, NOT panels, no dividing lines, no multiple horizons. Camera will start at the BOTTOM and steadily scroll toward the TOP. Draw one coherent world ascending from ground into outer space, every part a direct spatial continuation of the part below. Warm children's wax-crayon and colored-pencil storybook style, simple broad gently grainy shapes, restrained muted colors, no thick outlines, no photorealism. Bottom 0–12%: gentle flower meadow with low sage grass and very sparse small rounded trees, few cream and dusty rose flowers; the trees end by 18% from bottom. From 12–40% from bottom: natural muted pale blue open sky gradually deeper higher up, a few cream clouds, clear uncluttered central area. From 40–60%: airy blue high sky with sparse thinner cloud wisps, gradually deepening towards upper atmosphere. From 60–75%: subtle blue atmospheric glow smoothly fading to indigo, first very sparse small soft stars. Top 25%: calm muted deep navy-indigo space, tiny irregularly scattered cream stars, a small softly drawn crescent moon slightly off center in upper region. No second ground or planet horizon floating above the meadow; the transition to space is the same continuous sky darkening with altitude. Stars tiny and unobtrusive, no big glowing objects, no nebula clutter. Across whole panorama, keep central 60% low-contrast and sparsely decorated so small bright gameplay sprites remain readable. Full bleed opaque artwork, no border, no text, no captions, no UI, no game platforms, no coins, no characters. Essential: continuous upward environmental gradient, coherent scale and atmosphere, bottom to top ordering, no sudden changes, no seams, no repeated image panels.

## Финальные запросы аксессуаров

### flower

Use case: stylized-concept. Asset type: a single production 2D wearable accessory sprite for a simple crayon-drawn yellow cartoon cat game, not a mockup. Genuine transparent alpha background including all holes. Flat front view, centered with 10% transparent margin. Very simple friendly children's doodle, bold slightly irregular almost-black brown outlines, lightly grainy colored-pencil fills, flat bright colors. Clean readable silhouette at 20 pixels. No character, no head, no body, no paper backing, no shadows, no text, no photorealism, no 3D, no extra decorative objects. One small daisy hair clip: five rounded cream-white petals, cheerful golden-orange round center and two tiny mint-green leaves underneath. Compact flower, no long stem.

### glasses

Use case: stylized-concept. Asset type: a single production 2D wearable accessory sprite for a simple crayon-drawn yellow cartoon cat game, not a mockup. Genuine transparent alpha background including all holes. Flat front view, centered with 10% transparent margin. Very simple friendly children's doodle, bold slightly irregular almost-black brown outlines, lightly grainy colored-pencil fills, flat bright colors. Clean readable silhouette at 20 pixels. No character, no head, no body, no paper backing, no shadows, no text, no photorealism, no 3D, no extra decorative objects. One pair of round eyeglasses, two equally sized round thin warm dark brown rims joined with a short curved nose bridge, short outward temples. The lenses and interiors MUST be fully transparent, no lens glass tint, no reflections, no eyeballs. Wide horizontally, symmetric front view.

### tufts

Use case: stylized-concept. Asset type: a single production 2D wearable accessory sprite for a simple crayon-drawn yellow cartoon cat game, not a mockup. Genuine transparent alpha background including all holes. Flat front view, centered with 10% transparent margin. Very simple friendly children's doodle, bold slightly irregular almost-black brown outlines, lightly grainy colored-pencil fills, flat bright colors. Clean readable silhouette at 20 pixels. No character, no head, no body, no paper backing, no shadows, no text, no photorealism, no 3D, no extra decorative objects. One single small lynx ear tuft accessory, a compact fan of three curved pointed warm charcoal-brown furry strokes rising upward from a narrow base. Short central strand taller than the two sides, cozy hand-drawn organic silhouette. No ears or fur ball, just the tuft.

### star

Use case: stylized-concept. Asset type: a single production 2D wearable accessory sprite for a simple crayon-drawn yellow cartoon cat game, not a mockup. Genuine transparent alpha background including all holes. Flat front view, centered with 10% transparent margin. Very simple friendly children's doodle, bold slightly irregular almost-black brown outlines, lightly grainy colored-pencil fills, flat bright colors. Clean readable silhouette at 20 pixels. No character, no head, no body, no paper backing, no shadows, no text, no photorealism, no 3D, no extra decorative objects. One small cute five-point honey-yellow star hair clip with a dark brown doodle outline, a tiny apricot inner star highlight. Rounded star corners. No face, no sparkles, no stick or pin protruding.

