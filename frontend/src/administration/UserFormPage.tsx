import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router';
import { useApiClient } from '../api/ApiClientContext';
import type { Role } from '../auth/roles';
import buttons from '../components/buttons.module.css';
import { ConflictNotice } from '../components/ConflictNotice';
import { TextField } from '../components/Field';
import forms from '../components/forms.module.css';
import { LoadState } from '../components/Loading';
import { PageHeader } from '../components/PageHeader';
import { ProblemAlert } from '../components/ProblemAlert';
import { useRecordForm } from '../components/useRecordForm';
import { createUser, getUser, replaceRoles, updateUser, userKeys, useUser, type User } from './api';
import { RoleChoices } from './RoleChoices';

interface UserValues {
  email: string;
  displayName: string;
  password: string;
  roles: Role[];
}

const fields = ['email', 'displayName', 'password', 'roles'] as const;
const required = (what: string) => (value: string) => value.trim() !== '' || `Enter the ${what}.`;
const oneRole = (value: Role[]) => value.length > 0 || 'Choose at least one role.';

/** A new user of the hospital (US-01; ADR 0030): who they are, a first password they change at sign-in, and their roles. */
export function NewUserPage() {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const form = useForm<UserValues>({ defaultValues: { email: '', displayName: '', password: '', roles: [] } });
  const outcome = useRecordForm<User, UserValues>({ form, fields });
  const {
    register,
    control,
    formState: { errors },
  } = form;

  const creating = useMutation({
    mutationFn: (values: UserValues) => createUser(client, values),
    onMutate: outcome.clear,
    onSuccess: (user) => {
      queryClient.setQueryData(userKeys.one(user.id), user);
      void queryClient.invalidateQueries({ queryKey: [...userKeys.all, 'list'] });
      void navigate(`/administration/users/${user.id}`);
    },
    onError: outcome.failed,
  });

  return (
    <section aria-labelledby="page-title">
      <PageHeader title="New user" back={{ to: '/administration', label: 'Users' }} />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => creating.mutate(values))(event)}
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={[...fields]} />}
        <div className={forms.grid}>
          <TextField
            id="user-email"
            label="E-mail address"
            type="email"
            autoComplete="off"
            requiredMark
            hint="The address the user signs in with; another user of this hospital cannot have it."
            error={errors.email?.message}
            {...register('email', { validate: required('e-mail address') })}
          />
          <TextField
            id="user-display-name"
            label="Name"
            autoComplete="off"
            requiredMark
            error={errors.displayName?.message}
            {...register('displayName', { validate: required('name') })}
          />
          <TextField
            id="user-password"
            label="First password"
            type="password"
            autoComplete="new-password"
            requiredMark
            hint="12 to 128 characters. Tell it to the user by a safe channel: they choose their own at the first sign-in."
            error={errors.password?.message}
            {...register('password', {
              validate: (value) =>
                (value.length >= 12 && value.length <= 128) || 'The password has 12 to 128 characters.',
            })}
          />
        </div>
        <Controller
          control={control}
          name="roles"
          rules={{ validate: oneRole }}
          render={({ field }) => (
            <RoleChoices value={field.value} onChange={field.onChange} error={errors.roles?.message} />
          )}
        />
        <div className={forms.actions}>
          <button
            type="submit"
            className={buttons.primary}
            disabled={creating.isPending}
            aria-busy={creating.isPending}
          >
            {creating.isPending ? 'Creating…' : 'Create user'}
          </button>
          <Link to="/administration">Cancel</Link>
        </div>
      </form>
    </section>
  );
}

/** Corrects a user's address and name, and replaces their roles (ADR 0030): each part sent only when it changed. */
export function EditUserPage() {
  const { id = '' } = useParams();
  const user = useUser(id);

  return user.data === undefined ? (
    <section aria-labelledby="page-title">
      <PageHeader title="Edit user" back={{ to: `/administration/users/${id}`, label: 'User' }} />
      <LoadState error={user.error} what="the user" />
    </section>
  ) : (
    <EditUserForm key={user.data.id} initial={user.data} />
  );
}

function EditUserForm({ initial }: { initial: User }) {
  const client = useApiClient();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  // The user as the last save left them: a correction of the details changes the version the roles are sent with.
  const [saved, setSaved] = useState(initial);
  const valuesOf = (user: User): UserValues => ({
    email: user.email,
    displayName: user.displayName,
    password: '',
    roles: [...user.roles],
  });
  const form = useForm<UserValues>({ defaultValues: valuesOf(initial) });
  const outcome = useRecordForm<User, UserValues>({
    form,
    fields,
    initial,
    compared: [
      { label: 'E-mail address', value: (user) => user.email },
      { label: 'Name', value: (user) => user.displayName },
      { label: 'Roles', value: (user) => user.roles.join(', ') },
    ],
    valuesOf,
    reload: () => getUser(client, initial.id),
  });
  const {
    register,
    control,
    formState: { errors },
  } = form;

  const saving = useMutation({
    mutationFn: async ({ values, base }: { values: UserValues; base: User }) => {
      let current = base;
      if (values.email.trim() !== base.email || values.displayName.trim() !== base.displayName) {
        current = await updateUser(client, base.id, base.version, {
          email: values.email.trim(),
          displayName: values.displayName.trim(),
        });
        setSaved(current);
        outcome.rebase(current);
      }

      const sameRoles =
        values.roles.length === base.roles.length && values.roles.every((role) => base.roles.includes(role));
      if (!sameRoles) {
        current = await replaceRoles(client, current.id, current.version, values.roles);
      }

      return current;
    },
    onMutate: outcome.clear,
    onSuccess: (user) => {
      queryClient.setQueryData(userKeys.one(user.id), user);
      void queryClient.invalidateQueries({ queryKey: [...userKeys.all, 'list'] });
      void navigate(`/administration/users/${user.id}`);
    },
    onError: outcome.failed,
  });

  const base = outcome.base ?? saved;

  return (
    <section aria-labelledby="page-title">
      <PageHeader
        title={`Edit ${initial.displayName}`}
        back={{ to: `/administration/users/${initial.id}`, label: initial.displayName }}
      />
      <form
        className={forms.form}
        noValidate
        onSubmit={(event) => void form.handleSubmit((values) => saving.mutate({ values, base }))(event)}
      >
        {outcome.problem !== null && <ProblemAlert problem={outcome.problem} placedFields={[...fields]} />}
        {outcome.conflict !== null && (
          <ConflictNotice
            changes={outcome.conflict.changes}
            onLoadLatest={outcome.loadLatest}
            onKeepMine={outcome.keepMine}
          />
        )}
        <div className={forms.grid}>
          <TextField
            id="user-email"
            label="E-mail address"
            type="email"
            autoComplete="off"
            requiredMark
            error={errors.email?.message}
            {...register('email', { validate: required('e-mail address') })}
          />
          <TextField
            id="user-display-name"
            label="Name"
            autoComplete="off"
            requiredMark
            error={errors.displayName?.message}
            {...register('displayName', { validate: required('name') })}
          />
        </div>
        <Controller
          control={control}
          name="roles"
          rules={{ validate: oneRole }}
          render={({ field }) => (
            <RoleChoices value={field.value} onChange={field.onChange} error={errors.roles?.message} />
          )}
        />
        <div className={forms.actions}>
          <button type="submit" className={buttons.primary} disabled={saving.isPending} aria-busy={saving.isPending}>
            {saving.isPending ? 'Saving…' : 'Save'}
          </button>
          <Link to={`/administration/users/${initial.id}`}>Cancel</Link>
        </div>
      </form>
    </section>
  );
}
