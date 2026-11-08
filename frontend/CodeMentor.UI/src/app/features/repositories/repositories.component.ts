import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { KeyValuePipe } from '@angular/common';
import { RouterLink, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ApiErrorService } from '../../core/services/api-error.service';
import { RepositoryApiService } from '../../core/services/repository-api.service';
import { RepositoryProject } from '../../core/models/repository-project.model';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { RepositoryProjectListComponent } from './repository-project-list.component';

const githubRepositoryPattern = /^https:\/\/github\.com\/[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?\/[A-Za-z0-9_.-]+(?:\.git)?$/i;

@Component({
  imports: [KeyValuePipe, ReactiveFormsModule, RouterLink, PageHeadingComponent, RepositoryProjectListComponent],
  templateUrl: './repositories.component.html',
  styleUrl: './repositories.component.scss',
})
export class RepositoriesComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly repositoryApi = inject(RepositoryApiService);
  private readonly apiErrors = inject(ApiErrorService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  protected readonly busy = signal(false);
  protected readonly errorMessage = signal('');
  protected readonly validationErrors = signal<Readonly<Record<string, readonly string[]>>>({});
  protected readonly registeredProject = signal<RepositoryProject | null>(null);
  protected readonly lookupId = signal('');
  protected readonly lookupBusy = signal(false);
  protected readonly lookupError = signal('');
  protected readonly projects = signal<readonly RepositoryProject[]>([]);
  protected readonly projectsLoading = signal(true);
  protected readonly projectsError = signal('');

  protected readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    repositoryUrl: ['', [Validators.required, Validators.maxLength(2048), Validators.pattern(githubRepositoryPattern)]],
    description: ['', [Validators.maxLength(2000)]],
  });

  constructor() {
    this.refreshProjects();
  }

  protected submit(): void {
    this.errorMessage.set('');
    this.validationErrors.set({});
    this.registeredProject.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.repositoryApi
      .register({
        name: this.form.controls.name.value.trim(),
        repositoryUrl: this.form.controls.repositoryUrl.value.trim(),
        description: this.form.controls.description.value.trim() || null,
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (project) => {
          this.registeredProject.set(project);
          this.busy.set(false);
          this.refreshProjects();
        },
        error: (error: unknown) => {
          this.errorMessage.set(this.apiErrors.message(error));
          this.validationErrors.set(this.apiErrors.validationMessages(error));
          this.busy.set(false);
        },
      });
  }

  protected openById(): void {
    const id = this.lookupId().trim();
    this.lookupError.set('');
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(id)) {
      this.lookupError.set('Enter a valid repository project ID.');
      return;
    }

    this.lookupBusy.set(true);
    this.repositoryApi
      .getById(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (project) => {
          this.lookupBusy.set(false);
          void this.router.navigate(['/repositories', project.id]);
        },
        error: (error: unknown) => {
          this.lookupError.set(this.apiErrors.message(error));
          this.lookupBusy.set(false);
        },
      });
  }

  protected updateLookupId(event: Event): void {
    this.lookupId.set((event.target as HTMLInputElement).value);
  }

  protected refreshProjects(): void {
    this.projectsLoading.set(true);
    this.projectsError.set('');
    this.repositoryApi
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (projects) => {
          this.projects.set(projects);
          this.projectsLoading.set(false);
        },
        error: (error: unknown) => {
          this.projectsError.set(this.apiErrors.message(error));
          this.projectsLoading.set(false);
        },
      });
  }
}
