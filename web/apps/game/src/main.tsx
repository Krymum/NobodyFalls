import '@fontsource/barlow-condensed/500.css';
import '@fontsource/barlow-condensed/600.css';
import '@nobodyfalls/theme/tokens.css';
import '@nobodyfalls/layout/layout.css';
import './styles/global.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { App } from './App.tsx';
import { registerServiceWorker } from './core/pwa.ts';
import { detectLocale, initI18n, isLocale } from './i18n/index.ts';

const fromUrl = window.location.pathname.split('/')[1];
await initI18n(isLocale(fromUrl) ? fromUrl : detectLocale());
registerServiceWorker();

const root = document.getElementById('root');
if (root) {
  createRoot(root).render(
    <StrictMode>
      <App />
    </StrictMode>,
  );
}
