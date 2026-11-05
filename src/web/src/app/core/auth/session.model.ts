export type PrincipalType = 'PlatformAdministrator' | 'OrganizationUser';

export interface Session {
  id: string;
  principalType: PrincipalType;
  email: string;
  displayName: string;
  organizationId?: string;
  roles: string[];
  mustChangePassword: boolean;
  preferredLanguage: string;
}

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Extracts the stable error code from a problem type URI such as https://wabi-insight.dev/problems/locked_out. */
export function problemCode(problem: ProblemDetails | null | undefined): string | null {
  const type = problem?.type;
  if (!type) {
    return null;
  }
  const index = type.lastIndexOf('/');
  return index >= 0 ? type.substring(index + 1) : type;
}

export const ORGANIZATION_ADMIN_ROLE = 'OrganizationAdmin';
