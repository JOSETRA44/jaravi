import { Route, Routes } from 'react-router-dom';
import SiteLayout from './components/layout/SiteLayout.jsx';
import DocsLayout from './components/docs/DocsLayout.jsx';
import DocPage from './components/docs/DocPage.jsx';
import Landing from './pages/Landing.jsx';
import NotFound from './pages/NotFound.jsx';
import { docs } from './content/nav.js';

export default function App() {
  return (
    <Routes>
      <Route element={<SiteLayout />}>
        <Route index element={<Landing />} />

        {docs.map((doc) => (
          <Route
            key={doc.slug}
            path={doc.path}
            element={
              <DocsLayout>
                <DocPage doc={doc} />
              </DocsLayout>
            }
          />
        ))}

        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}
