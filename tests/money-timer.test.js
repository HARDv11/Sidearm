const Q = require(require('path').join(__dirname, '..', 'ui', 'query.js'));
const cfg = { bangs: { w: { name: 'Wiki', url: 'https://w/?q={q}' }, t: { name: 'Tr', url: 'https://t/?q={q}' } },
  rates: { date: '16.10.2026', uah: { USD: 41.5, EUR: 45.0, PLN: 10.4, GBP: 52.0, JPY: 0.27 } }, currency: 'UAH' };
let fails = 0;
function check(input, want) {
  const r = Q.resolve(input, cfg);
  const ok = Object.keys(want).every(k => r[k] === want[k]);
  if (!ok) { fails++; console.log('FAIL', JSON.stringify(input), JSON.stringify(r)); } else console.log('ok  ', input, '→', r.kind, r.display || r.label || r.url || '');
}
check('100 usd', { kind: 'money', display: '4 150.00 UAH' });
check('100$', { kind: 'money', display: '4 150.00 UAH' });
check('$100', { kind: 'money', display: '4 150.00 UAH' });
check('50 € in pln', { kind: 'money' });
check('1000 грн в usd', { kind: 'money', display: '24.10 USD' });
check('100 uah', { kind: 'money', display: '2.41 USD' });
check('5k usd to eur', { kind: 'money' });
check('2 pounds in usd', { kind: 'money' });
check('2 pounds in kg', { kind: 'units' });
check('10 pounds', { kind: 'search' });
check('300 spartans', { kind: 'search' });
check('5 ft in cm', { kind: 'units' });
check('15% of 80', { kind: 'calc' });
check('2^10', { kind: 'calc' });
check('timer 5m', { kind: 'timer', seconds: 300 });
check('timer 5 min boss', { kind: 'timer', seconds: 300, label: 'boss' });
check('timer 1:30 boss', { kind: 'timer', seconds: 90, label: 'boss' });
check('10 min timer', { kind: 'timer', seconds: 600 });
check('timer 1h30m', { kind: 'timer', seconds: 5400 });
check('timer 45s', { kind: 'timer', seconds: 45 });
check('таймер 3 хв', { kind: 'timer', seconds: 180 });
check('timer off', { kind: 'timer', cancel: true });
check('timer', { kind: 'search' });
check('t hello', { kind: 'bang' });
check('w timer', { kind: 'bang' });
check('2024 ford mustang', { kind: 'search' });
check('100 usd', { kind: 'money' });
// no rates: money falls through to search
const r = Q.resolve('100 usd', { bangs: {} });
if (r.kind !== 'search') { fails++; console.log('FAIL no-rates', r); } else console.log('ok   no rates → search');
console.log(fails ? fails + ' FAILED' : 'all passed');
