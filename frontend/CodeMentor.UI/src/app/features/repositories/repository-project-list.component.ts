import { DatePipe } from '@angular/common';
import { Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RepositoryIngestionResponse, RepositoryProject } from '../../core/models/repository-project.model';

@Component({
  imports: [DatePipe, RouterLink],
  selector: 'app-repository-project-list',
  templateUrl: './repository-project-list.component.html',
  styleUrl: './repository-project-list.component.scss',
})
export class RepositoryProjectListComponent {
  readonly projects = input<readonly RepositoryProject[]>([]);
  readonly loading = input(false);
  readonly error = input('');
  readonly ingestingRepositoryIds = input<ReadonlySet<string>>(new Set<string>());
  readonly ingestionResults = input<Readonly<Record<string, RepositoryIngestionResponse>>>({});
  readonly ingestionErrors = input<Readonly<Record<string, string>>>({});
  readonly refresh = output<void>();
  readonly ingestRepository = output<string>();
}
