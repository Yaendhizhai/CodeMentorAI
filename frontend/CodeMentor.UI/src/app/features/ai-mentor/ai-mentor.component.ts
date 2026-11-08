import { Component } from '@angular/core';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { FeatureUnavailableComponent } from '../../shared/components/feature-unavailable.component';

@Component({
  imports: [PageHeadingComponent, FeatureUnavailableComponent],
  templateUrl: './ai-mentor.component.html',
  styleUrl: './ai-mentor.component.scss',
})
export class AiMentorComponent {}
