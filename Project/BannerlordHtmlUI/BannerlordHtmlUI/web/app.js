const statusEl = document.getElementById('status');
const stateEl = document.getElementById('state');
const eventEl = document.getElementById('event');

const renderState = () => {
  stateEl.textContent = JSON.stringify(game.state.snapshot(), null, 2);
};

game.on('ready', snapshot => {
  statusEl.textContent = 'Framework runtime ready';
  eventEl.textContent = JSON.stringify({ ready: true, snapshot }, null, 2);
  renderState();
});

game.on('state:framework.status', value => {
  statusEl.textContent = String(value);
  renderState();
});

game.on('state:framework.testCounter', value => renderState());
game.on('framework:ping', value => {
  eventEl.textContent = JSON.stringify(value, null, 2);
});

document.getElementById('ping').onclick = async () => {
  try {
    const result = await game.call('framework.ping', { source: 'browser' });
    eventEl.textContent = JSON.stringify({ commandResult: result }, null, 2);
  } catch (e) { eventEl.textContent = String(e); }
};

document.getElementById('state-test').onclick = async () => {
  try {
    const result = await game.call('framework.incrementTestState');
    eventEl.textContent = JSON.stringify({ stateResult: result }, null, 2);
    renderState();
  } catch (e) { eventEl.textContent = String(e); }
};

document.getElementById('devtools').onclick = () => game.call('framework.openDevTools');
document.getElementById('reload').onclick = () => game.call('framework.reload');
document.getElementById('capture').onclick = () => game.call('framework.captureInput');
document.getElementById('release').onclick = () => game.call('framework.releaseInput');


function setUiInput(mode) {
    if (mode === 'captured') return game.call('framework.captureInput');
    if (mode === 'passive') return game.call('framework.passiveInput');
    return game.call('framework.releaseInput');
}
window.setUiInput = setUiInput;
