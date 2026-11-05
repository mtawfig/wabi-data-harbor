import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import {
  CreateOrganizationUser,
  Department,
  OrganizationUser,
  OrganizationUserStatus,
  Role,
  RoleTemplate,
  SaveDepartment,
  SaveRole,
  SaveRoleTemplate,
  SaveSection,
  Section,
  UpdateOrganizationUser,
} from './admin.models';
import { PagedResult } from './organizations.models';

/** Organization administration: departments, sections, roles and users of the signed-in organization. */
@Injectable({ providedIn: 'root' })
export class OrganizationAdminService {
  private readonly http = inject(HttpClient);

  listDepartments(includeInactive = true): Observable<Department[]> {
    return this.http.get<Department[]>('/api/departments', { params: new HttpParams().set('includeInactive', includeInactive) });
  }

  createDepartment(request: SaveDepartment): Observable<Department> {
    return this.http.post<Department>('/api/departments', request);
  }

  updateDepartment(id: string, request: SaveDepartment): Observable<Department> {
    return this.http.put<Department>(`/api/departments/${id}`, request);
  }

  listSections(departmentId?: string, includeInactive = true): Observable<Section[]> {
    let params = new HttpParams().set('includeInactive', includeInactive);
    if (departmentId) {
      params = params.set('departmentId', departmentId);
    }
    return this.http.get<Section[]>('/api/sections', { params });
  }

  createSection(request: SaveSection): Observable<Section> {
    return this.http.post<Section>('/api/sections', request);
  }

  updateSection(id: string, request: SaveSection): Observable<Section> {
    return this.http.put<Section>(`/api/sections/${id}`, request);
  }

  listRoles(): Observable<Role[]> {
    return this.http.get<Role[]>('/api/roles');
  }

  createRole(request: SaveRole): Observable<Role> {
    return this.http.post<Role>('/api/roles', request);
  }

  updateRole(id: string, request: SaveRole): Observable<Role> {
    return this.http.put<Role>(`/api/roles/${id}`, request);
  }

  deleteRole(id: string): Observable<void> {
    return this.http.delete<void>(`/api/roles/${id}`);
  }

  listUsers(page: number, pageSize: number, search?: string, status?: OrganizationUserStatus | null, departmentId?: string | null, roleCode?: string | null): Observable<PagedResult<OrganizationUser>> {
    let params = new HttpParams().set('page', page).set('pageSize', pageSize);
    if (search) {
      params = params.set('search', search);
    }
    if (status) {
      params = params.set('status', status);
    }
    if (departmentId) {
      params = params.set('departmentId', departmentId);
    }
    if (roleCode) {
      params = params.set('roleCode', roleCode);
    }
    return this.http.get<PagedResult<OrganizationUser>>('/api/users', { params });
  }

  getUser(id: string): Observable<OrganizationUser> {
    return this.http.get<OrganizationUser>(`/api/users/${id}`);
  }

  createUser(request: CreateOrganizationUser): Observable<OrganizationUser> {
    return this.http.post<OrganizationUser>('/api/users', request);
  }

  updateUser(id: string, request: UpdateOrganizationUser): Observable<OrganizationUser> {
    return this.http.put<OrganizationUser>(`/api/users/${id}`, request);
  }

  setUserStatus(id: string, status: 'Active' | 'Inactive'): Observable<OrganizationUser> {
    return this.http.post<OrganizationUser>(`/api/users/${id}/status`, { status });
  }

  resetUserPassword(id: string): Observable<{ userId: string; deliveredTo: string }> {
    return this.http.post<{ userId: string; deliveredTo: string }>(`/api/users/${id}/reset-password`, {});
  }
}

/** Platform-managed default role templates. */
@Injectable({ providedIn: 'root' })
export class RoleTemplatesService {
  private readonly http = inject(HttpClient);

  list(): Observable<RoleTemplate[]> {
    return this.http.get<RoleTemplate[]>('/api/platform/role-templates');
  }

  create(request: SaveRoleTemplate): Observable<RoleTemplate> {
    return this.http.post<RoleTemplate>('/api/platform/role-templates', request);
  }

  update(id: string, request: SaveRoleTemplate): Observable<RoleTemplate> {
    return this.http.put<RoleTemplate>(`/api/platform/role-templates/${id}`, request);
  }
}
