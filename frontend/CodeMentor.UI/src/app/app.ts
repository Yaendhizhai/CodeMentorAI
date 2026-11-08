import { Component, DestroyRef, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, startWith } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

interface NavigationItem {
  readonly label: string;
  readonly path: string;
  readonly icon: string;
}

@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly sidebarOpen = signal(false);
  protected readonly pageTitle = signal('Dashboard');
  protected readonly primaryNavigation: readonly NavigationItem[] = [
    { label: 'Dashboard', path: '/', icon: '⌂' },
    { label: 'Repositories', path: '/repositories', icon: '▤' },
    { label: 'Code Explorer', path: '/explorer', icon: '⌕' },
    { label: 'AI Mentor', path: '/mentor', icon: '✦' },
    { label: 'Analysis History', path: '/history', icon: '◷' },
  ];

  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        startWith(null),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        let route = this.router.routerState.root;
        while (route.firstChild) {
          route = route.firstChild;
        }
        this.pageTitle.set(String(route.snapshot.data['title'] ?? 'Dashboard'));
        this.sidebarOpen.set(false);
      });
  }

  protected toggleSidebar(): void {
    this.sidebarOpen.update((open) => !open);
  }

  protected closeSidebar(): void {
    this.sidebarOpen.set(false);
  }
}
