import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Dashboard',
    data: { title: 'Dashboard' },
    loadComponent: () => import('./features/dashboard/dashboard.component').then((module) => module.DashboardComponent),
  },
  {
    path: 'repositories',
    title: 'Repositories',
    data: { title: 'Repositories' },
    loadComponent: () =>
      import('./features/repositories/repositories.component').then((module) => module.RepositoriesComponent),
  },
  {
    path: 'repositories/:id',
    title: 'Repository details',
    data: { title: 'Repository details' },
    loadComponent: () =>
      import('./features/repository-details/repository-details.component').then(
        (module) => module.RepositoryDetailsComponent,
      ),
  },
  {
    path: 'explorer',
    title: 'Code Explorer',
    data: { title: 'Code Explorer' },
    loadComponent: () =>
      import('./features/code-explorer/code-explorer.component').then((module) => module.CodeExplorerComponent),
  },
  {
    path: 'mentor',
    title: 'AI Mentor',
    data: { title: 'AI Mentor' },
    loadComponent: () => import('./features/ai-mentor/ai-mentor.component').then((module) => module.AiMentorComponent),
  },
  {
    path: 'history',
    title: 'Analysis History',
    data: { title: 'Analysis History' },
    loadComponent: () =>
      import('./features/analysis-history/analysis-history.component').then(
        (module) => module.AnalysisHistoryComponent,
      ),
  },
  {
    path: 'settings',
    title: 'Settings',
    data: { title: 'Settings' },
    loadComponent: () => import('./features/settings/settings.component').then((module) => module.SettingsComponent),
  },
  { path: '**', redirectTo: '' },
];
