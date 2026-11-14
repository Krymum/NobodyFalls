import '@fontsource/barlow-condensed/500.css';
import '@fontsource/barlow-condensed/600.css';
import '@nobodyfalls/theme/tokens.css';
import './styles/global.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { SceneDemo } from './dev/SceneDemo.tsx';

const root = document.getElementById('root');
if (root) {
  // Step (e) ships the renderers and their demo; the game screens arrive in step (f).
  createRoot(root).render(
    <StrictMode>
      <SceneDemo />
    </StrictMode>,
  );
}
