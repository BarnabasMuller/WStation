'use strict';
// a téma azonnal (villanás nélkül); a mentett beállítás betöltés után felülírhatja.
// Külön fájlban, mert a Content-Security-Policy beágyazott szkriptet nem enged (2.2.7.1)
document.documentElement.dataset.theme = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
