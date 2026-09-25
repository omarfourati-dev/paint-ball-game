// SEO/GEO: Meta-Daten, strukturierte Daten, robots.txt, sitemap.xml, llms.txt (statisch geprüft, ohne Browser).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { STRINGS } from '../../web/js/i18n.js';

const ORIGIN = 'https://paint-ball-game.omarfourati.de';
const web = f => readFileSync(new URL(`../../web/${f}`, import.meta.url), 'utf8');
const index = web('index.html');
const decode = s => s.replace(/&amp;/g, '&').replace(/&quot;/g, '"').replace(/&lt;/g, '<').replace(/&gt;/g, '>');
const meta = (html, attr, name) => html.match(new RegExp(`<meta ${attr}="${name.replace(/[.:]/g, '\\$&')}" content="([^"]*)">`))?.[1];

function jsonLd(html) {
  const blocks = [...html.matchAll(/<script type="application\/ld\+json">([\s\S]*?)<\/script>/g)].map(m => JSON.parse(m[1]));
  assert.equal(blocks.length, 1, 'genau ein JSON-LD-Block');
  return blocks[0]['@graph'];
}

test('SEO: Landingpage mit Titel, Beschreibung, Canonical, robots, Autor', () => {
  assert.match(index, /<html lang="de"[ >]/);
  const title = index.match(/<title>([^<]+)<\/title>/)[1];
  assert.ok(title.includes('Paintball online spielen') && title.length <= 65, `Titel: ${title}`);
  assert.equal(STRINGS.de['landing.title'], title, 'landing.js setzt denselben Titel per i18n');
  const desc = meta(index, 'name', 'description');
  assert.ok(desc.length >= 120 && desc.length <= 155, `Beschreibung ${desc.length} Zeichen (höchstens 155)`);
  assert.match(index, new RegExp(`<link rel="canonical" href="${ORIGIN}/">`));
  assert.match(meta(index, 'name', 'robots'), /^index, follow/);
  assert.equal(meta(index, 'name', 'author'), 'Omar Fourati');
});

test('SEO: Open Graph und Twitter-Card vollständig, Bildmaße stimmen mit hero.jpg', () => {
  assert.equal(meta(index, 'property', 'og:url'), `${ORIGIN}/`);
  assert.equal(meta(index, 'property', 'og:locale'), 'de_DE');
  assert.equal(meta(index, 'property', 'og:site_name'), 'Paint-Ball');
  for (const p of ['og:title', 'og:description', 'og:image:alt']) assert.ok(meta(index, 'property', p), p);
  assert.equal(meta(index, 'property', 'og:image'), `${ORIGIN}/assets/landing/hero.jpg`);
  assert.equal(meta(index, 'property', 'og:image:width'), '1280');
  assert.equal(meta(index, 'property', 'og:image:height'), '720');
  assert.equal(meta(index, 'name', 'twitter:card'), 'summary_large_image');
  for (const p of ['twitter:title', 'twitter:description', 'twitter:image']) assert.ok(meta(index, 'name', p), p);
});

test('SEO: JSON-LD gültig – Spiel kostenlos, Browser, Mehrspieler, Herausgeber, Website', () => {
  const graph = jsonLd(index);
  const byType = t => graph.find(n => [].concat(n['@type']).includes(t));
  const game = byType('VideoGame');
  assert.ok([].concat(game['@type']).includes('WebApplication'));
  assert.equal(game.operatingSystem, 'Browser');
  assert.equal(game.offers.price, '0');
  assert.equal(game.offers.priceCurrency, 'EUR');
  assert.ok([].concat(game.playMode).includes('MultiPlayer'));
  assert.ok(game.genre.length > 0 && game.applicationCategory);
  assert.equal(game.numberOfPlayers.maxValue, 20, 'Pizzeria: MaxPlayers 20 (MapCatalog)');
  const person = byType('Person');
  assert.equal(person.url, 'https://omarfourati.de');
  assert.equal(game.publisher['@id'], person['@id']);
  assert.equal(byType('WebSite').url, `${ORIGIN}/`);
  assert.ok(!JSON.stringify(graph).includes('aggregateRating'), 'keine erfundenen Bewertungen');
});

test('SEO: FAQPage im JSON-LD entspricht wörtlich der sichtbaren FAQ und den DE-Texten', () => {
  const faq = jsonLd(index).find(n => n['@type'] === 'FAQPage');
  const visible = [...index.matchAll(/<summary data-i18n="(landing\.faq\.[a-z]+)\.q">([^<]+)<\/summary>\s*<p data-i18n="\1\.a">([^<]+)<\/p>/g)]
    .map(m => ({ key: m[1], q: decode(m[2]), a: decode(m[3]) }));
  assert.ok(visible.length >= 4 && visible.length <= 6, 'vier bis sechs Fragen sichtbar');
  assert.deepEqual(faq.mainEntity.map(e => [e.name, e.acceptedAnswer.text]), visible.map(v => [v.q, v.a]));
  for (const v of visible) {
    assert.equal(STRINGS.de[`${v.key}.q`], v.q, `${v.key}.q DE`);
    assert.equal(STRINGS.de[`${v.key}.a`], v.a, `${v.key}.a DE`);
    assert.ok(STRINGS.en[`${v.key}.q`] && STRINGS.en[`${v.key}.a`], `${v.key} EN`);
  }
});

test('SEO: /play ist noindex, Rechtstexte noindex (Privatanschrift) mit eigenem Titel, Beschreibung und Canonical', () => {
  assert.match(web('play.html'), /<meta name="robots" content="noindex">/);
  for (const [file, path] of [['impressum.html', '/impressum'], ['datenschutz.html', '/datenschutz']]) {
    const html = web(file);
    assert.match(html, /<title>[^<]+ – Paint-Ball<\/title>/, file);
    assert.ok(meta(html, 'name', 'description')?.length > 50, `${file} Beschreibung`);
    assert.match(html, new RegExp(`<link rel="canonical" href="${ORIGIN}${path}">`));
    assert.match(html, /<meta name="robots" content="noindex">/, `${file} bleibt noindex – Privatanschrift`);
  }
});

test('SEO: robots.txt sperrt nur /api/, erlaubt KI-Crawler, verweist auf die Sitemap', () => {
  const robots = web('robots.txt');
  assert.match(robots, /^Disallow: \/api\/$/m);
  assert.doesNotMatch(robots, /^Disallow: \/(?!api\/)/m, 'nichts außer /api/ gesperrt');
  for (const bot of ['GPTBot', 'OAI-SearchBot', 'ChatGPT-User', 'PerplexityBot', 'ClaudeBot', 'Google-Extended', 'Bingbot', 'Applebot-Extended'])
    assert.match(robots, new RegExp(`^User-agent: ${bot}$`, 'm'), bot);
  assert.match(robots, new RegExp(`^Sitemap: ${ORIGIN}/sitemap.xml$`, 'm'));
});

test('SEO: sitemap.xml enthält nur / mit lastmod – nicht /play und keine noindex-Rechtstexte', () => {
  const xml = web('sitemap.xml');
  const locs = [...xml.matchAll(/<loc>([^<]+)<\/loc>/g)].map(m => m[1]);
  assert.deepEqual(locs, [`${ORIGIN}/`]);
  assert.equal([...xml.matchAll(/<lastmod>2026-09-25<\/lastmod>/g)].length, 1);
  assert.match(xml, /xmlns="http:\/\/www\.sitemaps\.org\/schemas\/sitemap\/0\.9"/);
});

test('GEO: llms.txt im llmstxt.org-Format mit Fakten und Links', () => {
  const llms = web('llms.txt');
  assert.match(llms, /^# Paint-Ball\n\n> .+/, 'H1 und Zusammenfassung als Blockzitat');
  for (const s of ['kostenlos', 'Browser', 'PWA', 'Capture the Flag', 'Lagerhaus', 'Handy'])
    assert.ok(llms.includes(s), s);
  for (const path of ['/', '/play', '/impressum', '/datenschutz'])
    assert.ok(llms.includes(`](${ORIGIN}${path})`), `Link ${path}`);
});

test('GEO: zitierfähiger Einleitungssatz sichtbar im Hero, DE und EN', () => {
  assert.match(index, /<p class="intro" data-i18n="landing.intro">Paint-Ball ist ein kostenloses Online-Multiplayer-Paintball-Spiel, das direkt im Browser läuft/);
  assert.ok(STRINGS.en['landing.intro'].startsWith('Paint-Ball is a free online multiplayer paintball game'));
});

test('Datenschutz: AdSense-Abschnitt mit Anbieter, Einwilligung, nicht personalisierten Anzeigen, Drittland und Widerruf', () => {
  const privacy = web('datenschutz.html');
  for (const s of ['Werbung mit Google AdSense', 'Google Ireland Limited', 'Einwilligung', 'eingeschränkte Anzeigen',
    'EU-US Data Privacy Framework', 'Datenschutzeinstellungen', 'widerrufen', '§ 25 Abs. 1 TDDDG', 'Art. 6 Abs. 1 lit. a DSGVO',
    'Art. 6 Abs. 1 lit. f DSGVO: Mein berechtigtes Interesse ist, die gesetzlich vorgeschriebene Einwilligung', 'IP-Adresse',
    'Nur mit deiner Einwilligung', 'Ohne Einwilligung',
    'Werbung wird über Google AdSense eingeblendet, sofern aktiviert'])
    assert.ok(privacy.includes(s), `Datenschutz enthält ${s}`);
  assert.ok(!privacy.includes('keine Werbung'), 'Aussage „keine Werbung“ korrigiert');
  assert.ok(!web('llms.txt').includes('keine Werbung'), 'llms.txt ebenso');
});

test('SEO: Bot-Angaben entsprechen Room.cs – Auffüllen nur bei schnellen Matches (8 Team, 6 FFA), privat bestimmt der Host', () => {
  const graph = jsonLd(index);
  const faq = graph.find(n => n['@type'] === 'FAQPage');
  const players = faq.mainEntity.find(e => e.name.startsWith('Wie viele Spieler')).acceptedAnswer.text;
  for (const txt of [players, STRINGS.de['landing.faq.players.a'], web('llms.txt')]) {
    assert.ok(!/freie Plätze werden mit Bots gefüllt/i.test(txt), 'keine pauschale Aussage „freie Plätze mit Bots“');
    assert.ok(txt.includes('Bei schnellen Matches füllen Bots auf 8 Spieler (Team-Modi) bzw. 6 (Jeder gegen jeden) auf'), 'Quick-Match-Auffüllung');
    assert.ok(txt.includes('in privaten Räumen legt der Host die Bots fest'), 'private Räume');
  }
  const en = STRINGS.en['landing.faq.players.a'];
  assert.ok(en.includes('bots top up to 8 players (team modes) or 6 (free for all)') && en.includes('private rooms the host decides'), 'EN');
  assert.ok(!/free slots are filled/i.test(en));
});
