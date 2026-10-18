const Q = require(require('path').join(__dirname, '..', 'ui', 'query.js'));
const cfg = { searchUrl: 'https://www.google.com/search?q={q}', bangs: {
  w:{name:'Wikipedia (EN)',url:'https://en.wikipedia.org/w/index.php?search={q}'},
  wu:{name:'Wikipedia (UK)',url:'https://uk.wikipedia.org/w/index.php?search={q}'},
  t:{name:'Translate',url:'https://translate.google.com/?sl=auto&tl={tl}&text={q}&op=translate'},
  ddg:{name:'DDG',url:'https://duckduckgo.com/?q={q}'}, i:{name:'Images',url:'x?q={q}'}, d:{name:'Dict',url:'d?q=define+{q}'} } };
const cases = [
 ['120*3.5','calc','420'], ['(1500-320)/4','calc','295'], ['2^10','calc','1024'], ['-2^2','calc','-4'],
 ['sqrt(16)+1','calc','5'], ['2pi','calc','6.28318530718'], ['15% of 80','calc','12'], ['3,5*2','calc','7'],
 ['5!','calc','120'], ['10 mod 3','calc','1'], ['1e3+1','calc','1001'], ['7 x 6','calc','42'], ['100/3','calc','33.3333333333'],
 ['1945','search'], ['1945 1991','search'], ['2024-10-15','search'], ['ak-74 effective range','search'], ['t-90m','search'],
 ['+380 67 123 4567','search'], ['e','search'], ['-5','search'], ['pi','search'],
 ['5 ft in cm','units','152.4'], ['30 mph in km/h','units','48.2803'], ['2 lb in kg','units','0.907185'],
 ['100 f in c','units','37.7778'], ['0 °C to °F','units','32'], ['1 gib in mb','units','1073.74'], ['5 ft in kg','search'],
 ['w treaty of westphalia','bang'], ['wu Гетьманщина','bang'], ['!w kyiv','bang'], ['t sitrep','bang'], ['t привіт','bang'],
 ['d irreverent','bang'], ['w','search'], ['youtube.com','url'], ['node.js','search'], ['localhost:3000/x','url'],
 ['https://example.org/a b','search'], ['www.zen-browser.app','url'], ['en.wikipedia.org/wiki/Kyiv','url'], ['3.5','search'],
 ['ddg ukraine eu accession timeline','bang'], ['', 'empty'],
];
let fail=0;
for (const [inp, kind, val] of cases) {
  const r = Q.resolve(inp, cfg);
  let ok = r.kind === kind && (val === undefined || r.copy === val);
  if (!ok) { fail++; console.log('FAIL', JSON.stringify(inp), '→', JSON.stringify(r)); }
}
console.log(Q.resolve('t sitrep',cfg).url); console.log(Q.resolve('t привіт',cfg).url);
console.log(Q.resolve('w treaty of westphalia',cfg).url);
console.log(JSON.stringify(Q.bangSuggestions('w', cfg.bangs)), JSON.stringify(Q.bangSuggestions('w ', cfg.bangs)));
console.log(Q.prettyNumber(1234567.891), Q.resolve('5 ft in cm',cfg).label);
console.log(fail ? fail+' failures' : 'all '+cases.length+' passed');
