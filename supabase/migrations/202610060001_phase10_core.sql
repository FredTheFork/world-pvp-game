-- Phase 10 control-plane schema for Supabase Auth/Postgres.
-- This stores game/account metadata only. It intentionally does NOT store map tiles,
-- Google-derived geometry, imagery, or a real-world map database.

begin;

create extension if not exists pgcrypto;
create schema if not exists private;
revoke all on schema private from public, anon, authenticated;
grant usage on schema private to service_role;

create table if not exists public.users (
    id uuid primary key references auth.users (id) on delete cascade,
    username text,
    avatar_url text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint users_username_format check (
        username is null or username ~ '^[A-Za-z0-9_]{3,24}$'
    ),
    constraint users_avatar_url_length check (
        avatar_url is null or char_length(avatar_url) <= 512
    )
);
create unique index if not exists users_username_lower_unique
    on public.users (lower(username)) where username is not null;

create table if not exists public.statistics (
    user_id uuid primary key references public.users (id) on delete cascade,
    matches_played bigint not null default 0 check (matches_played >= 0),
    kills bigint not null default 0 check (kills >= 0),
    deaths bigint not null default 0 check (deaths >= 0),
    wins bigint not null default 0 check (wins >= 0),
    updated_at timestamptz not null default now()
);

create table if not exists public.matches (
    id uuid primary key default gen_random_uuid(),
    host_user_id uuid not null references public.users (id) on delete restrict,
    state text not null default 'CREATING' check (
        state in ('CREATING', 'LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE', 'FINISHED', 'CLOSED')
    ),
    centre_latitude double precision not null check (centre_latitude between -90 and 90),
    centre_longitude double precision not null check (centre_longitude between -180 and 180),
    centre_altitude_meters double precision not null check (
        centre_altitude_meters between -12000 and 100000
    ),
    radius_meters double precision not null check (radius_meters > 0 and radius_meters <= 2000),
    maximum_players integer not null check (maximum_players between 2 and 32),
    game_mode text not null default 'Battle' check (game_mode = 'Battle'),
    join_code_hash text not null unique check (join_code_hash ~ '^[0-9a-f]{64}$'),
    is_locked boolean not null default false,
    network_session_id text,
    server_instance_id text,
    server_heartbeat_at timestamptz,
    network_endpoint jsonb,
    end_requested_at timestamptz,
    state_version integer not null default 0 check (state_version >= 0),
    created_at timestamptz not null default now(),
    lobby_at timestamptz,
    loading_at timestamptz,
    countdown_at timestamptz,
    live_at timestamptz,
    finished_at timestamptz,
    closed_at timestamptz,
    updated_at timestamptz not null default now(),
    constraint matches_network_session_length check (
        network_session_id is null or char_length(network_session_id) <= 160
    ),
    constraint matches_server_instance_length check (
        server_instance_id is null or char_length(server_instance_id) <= 160
    )
);
create index if not exists matches_state_created_idx on public.matches (state, created_at desc);
create index if not exists matches_host_created_idx on public.matches (host_user_id, created_at desc);

create table if not exists public.match_members (
    match_id uuid not null references public.matches (id) on delete cascade,
    user_id uuid not null references public.users (id) on delete restrict,
    role text not null default 'PLAYER' check (role in ('HOST', 'PLAYER')),
    status text not null default 'ACTIVE' check (
        status in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED', 'KICKED', 'LEFT')
    ),
    joined_at timestamptz not null default now(),
    last_seen_at timestamptz not null default now(),
    disconnected_at timestamptz,
    kicked_at timestamptz,
    eliminated_at timestamptz,
    primary key (match_id, user_id)
);
create index if not exists match_members_user_idx on public.match_members (user_id, joined_at desc);
create index if not exists match_members_active_idx on public.match_members (match_id, status);

create table if not exists public.match_results (
    id uuid primary key default gen_random_uuid(),
    match_id uuid not null references public.matches (id) on delete restrict,
    user_id uuid not null references public.users (id) on delete restrict,
    kills integer not null default 0 check (kills between 0 and 10000),
    deaths integer not null default 0 check (deaths between 0 and 10000),
    placement integer not null check (placement between 1 and 32),
    won boolean not null default false,
    score integer not null default 0 check (score between -1000000 and 1000000),
    created_at timestamptz not null default now(),
    unique (match_id, user_id)
);
create index if not exists match_results_user_created_idx on public.match_results (user_id, created_at desc);

create table if not exists public.sessions (
    id uuid primary key default gen_random_uuid(),
    match_id uuid not null references public.matches (id) on delete cascade,
    user_id uuid not null references public.users (id) on delete restrict,
    status text not null default 'PENDING' check (
        status in ('PENDING', 'CONNECTED', 'DISCONNECTED', 'ELIMINATED', 'CLOSED')
    ),
    connection_ticket_hash text unique check (
        connection_ticket_hash is null or connection_ticket_hash ~ '^[0-9a-f]{64}$'
    ),
    connection_ticket_expires_at timestamptz,
    reconnect_token_hash text unique check (
        reconnect_token_hash is null or reconnect_token_hash ~ '^[0-9a-f]{64}$'
    ),
    reconnect_expires_at timestamptz,
    last_seen_at timestamptz not null default now(),
    last_server_state jsonb,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint sessions_server_state_size check (
        last_server_state is null or octet_length(last_server_state::text) <= 16384
    )
);
create index if not exists sessions_match_status_idx on public.sessions (match_id, status);
create index if not exists sessions_user_updated_idx on public.sessions (user_id, updated_at desc);

create table if not exists public.reports (
    id uuid primary key default gen_random_uuid(),
    reporter_user_id uuid not null references public.users (id) on delete restrict,
    target_user_id uuid not null references public.users (id) on delete restrict,
    match_id uuid references public.matches (id) on delete set null,
    category text not null check (category in ('CHEATING', 'HARASSMENT', 'ABUSE', 'SPAM', 'OTHER')),
    description text not null default '' check (char_length(description) <= 1000),
    status text not null default 'OPEN' check (status in ('OPEN', 'REVIEWING', 'RESOLVED', 'DISMISSED')),
    created_at timestamptz not null default now(),
    resolved_at timestamptz,
    resolved_by uuid references public.users (id) on delete set null,
    constraint reports_no_self_report check (reporter_user_id <> target_user_id)
);
create index if not exists reports_review_idx on public.reports (status, created_at);
create index if not exists reports_target_idx on public.reports (target_user_id, created_at desc);

create table if not exists public.bans (
    id uuid primary key default gen_random_uuid(),
    user_id uuid not null references public.users (id) on delete restrict,
    match_id uuid references public.matches (id) on delete cascade,
    created_by uuid references public.users (id) on delete set null,
    reason text not null check (char_length(reason) between 3 and 500),
    starts_at timestamptz not null default now(),
    expires_at timestamptz,
    lifted_at timestamptz,
    lifted_by uuid references public.users (id) on delete set null,
    created_at timestamptz not null default now(),
    constraint bans_expiry_after_start check (expires_at is null or expires_at > starts_at)
);
create index if not exists bans_active_user_idx on public.bans (user_id, expires_at)
    where lifted_at is null;

create table if not exists public.user_blocks (
    user_id uuid not null references public.users (id) on delete cascade,
    blocked_user_id uuid not null references public.users (id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (user_id, blocked_user_id),
    constraint user_blocks_not_self check (user_id <> blocked_user_id)
);

create table if not exists public.user_mutes (
    user_id uuid not null references public.users (id) on delete cascade,
    muted_user_id uuid not null references public.users (id) on delete cascade,
    created_at timestamptz not null default now(),
    primary key (user_id, muted_user_id),
    constraint user_mutes_not_self check (user_id <> muted_user_id)
);

create table if not exists public.chat_messages (
    id bigint generated always as identity primary key,
    match_id uuid not null references public.matches (id) on delete cascade,
    user_id uuid not null references public.users (id) on delete restrict,
    body text not null check (char_length(body) between 1 and 500),
    created_at timestamptz not null default now()
);
create index if not exists chat_messages_match_created_idx on public.chat_messages (match_id, created_at desc);

create table if not exists public.match_events (
    id bigint generated always as identity primary key,
    match_id uuid not null references public.matches (id) on delete cascade,
    actor_user_id uuid references public.users (id) on delete set null,
    event_type text not null check (char_length(event_type) between 1 and 64),
    event_data jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    constraint match_events_payload_size check (octet_length(event_data::text) <= 8192)
);
create index if not exists match_events_match_created_idx on public.match_events (match_id, created_at);

create table if not exists private.rate_limits (
    subject_hash text not null,
    bucket text not null,
    window_started_at timestamptz not null,
    request_count integer not null check (request_count >= 1),
    updated_at timestamptz not null default now(),
    primary key (subject_hash, bucket)
);
revoke all on table private.rate_limits from public, anon, authenticated;
grant select, insert, update, delete on table private.rate_limits to service_role;

create or replace function public.handle_new_auth_user()
returns trigger
language plpgsql
security definer
set search_path = pg_catalog, public, pg_temp
as $$
begin
    insert into public.users (id, username)
    values (
        new.id,
        case
            when coalesce(new.raw_user_meta_data ->> 'username', '') ~ '^[A-Za-z0-9_]{3,24}$'
            then new.raw_user_meta_data ->> 'username'
            else null
        end
    )
    on conflict (id) do nothing;
    insert into public.statistics (user_id) values (new.id) on conflict (user_id) do nothing;
    return new;
end;
$$;
revoke all on function public.handle_new_auth_user() from public, anon, authenticated;

drop trigger if exists on_auth_user_created_world_pvp on auth.users;
create trigger on_auth_user_created_world_pvp
after insert on auth.users
for each row execute function public.handle_new_auth_user();

create or replace function public.consume_rate_limit(
    p_subject_hash text,
    p_bucket text,
    p_limit integer,
    p_window_seconds integer
)
returns table (allowed boolean, retry_after_seconds integer)
language plpgsql
security definer
set search_path = pg_catalog, private, public, auth, pg_temp
as $$
declare
    v_now timestamptz := clock_timestamp();
    v_window_started timestamptz;
    v_count integer;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_subject_hash is null or char_length(p_subject_hash) < 16 or char_length(p_subject_hash) > 128 or
       p_bucket is null or p_bucket !~ '^[a-z0-9_-]{1,64}$' or
       p_limit is null or p_limit < 1 or p_limit > 10000 or
       p_window_seconds is null or p_window_seconds < 1 or p_window_seconds > 86400 then
        raise exception 'INVALID_RATE_LIMIT' using errcode = '22023';
    end if;

    insert into private.rate_limits as existing (
        subject_hash, bucket, window_started_at, request_count, updated_at
    ) values (p_subject_hash, p_bucket, v_now, 1, v_now)
    on conflict (subject_hash, bucket) do update set
        window_started_at = case
            when v_now >= existing.window_started_at + (p_window_seconds * interval '1 second') then v_now
            else existing.window_started_at
        end,
        request_count = case
            when v_now >= existing.window_started_at + (p_window_seconds * interval '1 second') then 1
            else existing.request_count + 1
        end,
        updated_at = v_now
    returning existing.window_started_at, existing.request_count
    into v_window_started, v_count;

    return query select
        (v_count <= p_limit),
        case when v_count <= p_limit then 0
             else greatest(1, ceil(extract(epoch from (v_window_started + (p_window_seconds * interval '1 second') - v_now)))::integer)
        end;
end;
$$;

create or replace function public.create_match(
    p_host_user_id uuid,
    p_centre_latitude double precision,
    p_centre_longitude double precision,
    p_centre_altitude_meters double precision,
    p_radius_meters double precision,
    p_maximum_players integer,
    p_join_code_hash text
)
returns public.matches
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_match public.matches;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_host_user_id is null or
       p_centre_latitude is null or p_centre_latitude not between -90 and 90 or
       p_centre_longitude is null or p_centre_longitude not between -180 and 180 or
       p_centre_altitude_meters is null or p_centre_altitude_meters not between -12000 and 100000 or
       p_radius_meters is null or p_radius_meters <= 0 or p_radius_meters > 2000 or
       p_maximum_players is null or p_maximum_players not between 2 and 32 or
       p_join_code_hash is null or p_join_code_hash !~ '^[0-9a-f]{64}$' then
        raise exception 'INVALID_MATCH_CONFIGURATION' using errcode = '22023';
    end if;
    if not exists (select 1 from public.users u where u.id = p_host_user_id) then
        raise exception 'USER_NOT_FOUND' using errcode = '23503';
    end if;
    if exists (
        select 1 from public.bans b
        where b.user_id = p_host_user_id and b.match_id is null and b.lifted_at is null
          and b.starts_at <= now() and (b.expires_at is null or b.expires_at > now())
    ) then
        raise exception 'USER_BANNED' using errcode = '42501';
    end if;

    insert into public.matches (
        host_user_id, centre_latitude, centre_longitude, centre_altitude_meters,
        radius_meters, maximum_players, join_code_hash
    ) values (
        p_host_user_id, p_centre_latitude, p_centre_longitude, p_centre_altitude_meters,
        p_radius_meters, p_maximum_players, p_join_code_hash
    ) returning * into v_match;

    insert into public.match_members (match_id, user_id, role, status)
    values (v_match.id, p_host_user_id, 'HOST', 'ACTIVE');
    insert into public.match_events (match_id, actor_user_id, event_type)
    values (v_match.id, p_host_user_id, 'MATCH_CREATED');
    return v_match;
end;
$$;

create or replace function public.transition_match(p_match_id uuid, p_to_state text)
returns public.matches
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_match public.matches;
    v_allowed boolean := false;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    select * into v_match from public.matches where id = p_match_id for update;
    if not found then
        raise exception 'MATCH_NOT_FOUND' using errcode = 'P0002';
    end if;

    v_allowed := (v_match.state = 'CREATING' and p_to_state in ('LOBBY', 'CLOSED')) or
                 (v_match.state = 'LOBBY' and p_to_state in ('LOADING', 'CLOSED')) or
                 (v_match.state = 'LOADING' and p_to_state in ('COUNTDOWN', 'CLOSED')) or
                 (v_match.state = 'COUNTDOWN' and p_to_state in ('LIVE', 'CLOSED')) or
                 (v_match.state = 'LIVE' and p_to_state in ('FINISHED', 'CLOSED')) or
                 (v_match.state = 'FINISHED' and p_to_state = 'CLOSED');
    if not v_allowed then
        raise exception 'INVALID_MATCH_TRANSITION:%->%', v_match.state, p_to_state using errcode = '22023';
    end if;

    update public.matches set
        state = p_to_state,
        state_version = state_version + 1,
        updated_at = now(),
        lobby_at = case when p_to_state = 'LOBBY' then now() else lobby_at end,
        loading_at = case when p_to_state = 'LOADING' then now() else loading_at end,
        countdown_at = case when p_to_state = 'COUNTDOWN' then now() else countdown_at end,
        live_at = case when p_to_state = 'LIVE' then now() else live_at end,
        finished_at = case when p_to_state = 'FINISHED' then now() else finished_at end,
        closed_at = case when p_to_state = 'CLOSED' then now() else closed_at end
    where id = p_match_id returning * into v_match;
    insert into public.match_events (match_id, event_type, event_data)
    values (p_match_id, 'STATE_CHANGED', jsonb_build_object('to', p_to_state, 'version', v_match.state_version));
    return v_match;
end;
$$;

create or replace function public.join_match_by_code(
    p_user_id uuid,
    p_join_code_hash text,
    p_connection_ticket_hash text,
    p_reconnect_token_hash text
)
returns table (
    match_id uuid,
    session_id uuid,
    state text,
    centre_latitude double precision,
    centre_longitude double precision,
    centre_altitude_meters double precision,
    radius_meters double precision,
    maximum_players integer,
    ticket_expires_at timestamptz
)
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_match public.matches;
    v_existing text;
    v_active_count integer;
    v_session_id uuid;
    v_expiry timestamptz := now() + interval '60 seconds';
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_user_id is null or p_join_code_hash is null or
       p_join_code_hash !~ '^[0-9a-f]{64}$' or p_connection_ticket_hash is null or
       p_connection_ticket_hash !~ '^[0-9a-f]{64}$' or p_reconnect_token_hash is null or
       p_reconnect_token_hash !~ '^[0-9a-f]{64}$' then
        raise exception 'INVALID_TICKET' using errcode = '22023';
    end if;
    select * into v_match from public.matches where join_code_hash = p_join_code_hash for update;
    if not found or v_match.state <> 'LOBBY' or v_match.is_locked then
        raise exception 'MATCH_UNAVAILABLE' using errcode = 'P0002';
    end if;
    if exists (
        select 1 from public.bans b
        where b.user_id = p_user_id and (b.match_id is null or b.match_id = v_match.id)
          and b.lifted_at is null and b.starts_at <= now() and (b.expires_at is null or b.expires_at > now())
    ) then
        raise exception 'USER_BANNED' using errcode = '42501';
    end if;

    select status into v_existing from public.match_members
    where match_members.match_id = v_match.id and user_id = p_user_id for update;
    if found then
        if v_existing = 'KICKED' then raise exception 'PLAYER_KICKED' using errcode = '42501'; end if;
        if v_existing in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED') then
            raise exception 'RECONNECT_REQUIRED' using errcode = '23505';
        end if;
        raise exception 'PLAYER_ALREADY_JOINED' using errcode = '23505';
    end if;

    select count(*) into v_active_count from public.match_members
    where match_members.match_id = v_match.id and status in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED');
    if v_active_count >= v_match.maximum_players then
        raise exception 'MATCH_FULL' using errcode = '23514';
    end if;
    if not exists (select 1 from public.users u where u.id = p_user_id) then
        raise exception 'USER_NOT_FOUND' using errcode = '23503';
    end if;

    insert into public.match_members (match_id, user_id, role, status)
    values (v_match.id, p_user_id, 'PLAYER', 'ACTIVE');
    insert into public.sessions (
        match_id, user_id, status, connection_ticket_hash,
        connection_ticket_expires_at, reconnect_token_hash, reconnect_expires_at
    ) values (
        v_match.id, p_user_id, 'PENDING', p_connection_ticket_hash,
        v_expiry, p_reconnect_token_hash, now() + interval '10 minutes'
    ) returning id into v_session_id;

    return query select v_match.id, v_session_id, v_match.state,
        v_match.centre_latitude, v_match.centre_longitude, v_match.centre_altitude_meters,
        v_match.radius_meters, v_match.maximum_players, v_expiry;
end;
$$;

create or replace function public.consume_connection_ticket(p_ticket_hash text)
returns table (session_id uuid, match_id uuid, user_id uuid)
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_session public.sessions;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    select * into v_session from public.sessions
    where connection_ticket_hash = p_ticket_hash
      and connection_ticket_expires_at > now()
      and status = 'PENDING'
    for update;
    if not found then
        raise exception 'TICKET_EXPIRED_OR_USED' using errcode = '42501';
    end if;
    if not exists (
        select 1 from public.matches m
        where m.id = v_session.match_id and m.state in ('LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE')
    ) or not exists (
        select 1 from public.match_members mm
        where mm.match_id = v_session.match_id and mm.user_id = v_session.user_id
          and mm.status in ('ACTIVE', 'DISCONNECTED')
    ) or exists (
        select 1 from public.bans b
        where b.user_id = v_session.user_id and (b.match_id is null or b.match_id = v_session.match_id)
          and b.lifted_at is null and b.starts_at <= now() and (b.expires_at is null or b.expires_at > now())
    ) then
        raise exception 'TICKET_REVOKED' using errcode = '42501';
    end if;
    update public.sessions set
        status = 'CONNECTED', connection_ticket_hash = null,
        connection_ticket_expires_at = null, last_seen_at = now(), updated_at = now()
    where id = v_session.id;
    update public.match_members set status = 'ACTIVE', last_seen_at = now(), disconnected_at = null
    where match_id = v_session.match_id and user_id = v_session.user_id and status = 'DISCONNECTED';
    return query select v_session.id, v_session.match_id, v_session.user_id;
end;
$$;

create or replace function public.redeem_reconnect_ticket(
    p_reconnect_token_hash text,
    p_new_reconnect_token_hash text
)
returns table (session_id uuid, match_id uuid, user_id uuid, last_server_state jsonb)
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_session public.sessions;
    v_match_state text;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_reconnect_token_hash !~ '^[0-9a-f]{64}$' or p_new_reconnect_token_hash !~ '^[0-9a-f]{64}$' then
        raise exception 'INVALID_TICKET' using errcode = '22023';
    end if;
    select s.* into v_session from public.sessions s
    where s.reconnect_token_hash = p_reconnect_token_hash
      and s.reconnect_expires_at > now()
      and s.status in ('DISCONNECTED', 'ELIMINATED')
    for update;
    if not found then
        raise exception 'RECONNECT_UNAVAILABLE' using errcode = '42501';
    end if;
    select state into v_match_state from public.matches where id = v_session.match_id for update;
    if v_match_state not in ('LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE') then
        raise exception 'MATCH_NOT_RECONNECTABLE' using errcode = '42501';
    end if;
    if not exists (
        select 1 from public.match_members mm
        where mm.match_id = v_session.match_id and mm.user_id = v_session.user_id
          and mm.status in ('DISCONNECTED', 'ELIMINATED')
    ) then
        raise exception 'RECONNECT_UNAVAILABLE' using errcode = '42501';
    end if;
    if exists (
        select 1 from public.bans b
        where b.user_id = v_session.user_id and (b.match_id is null or b.match_id = v_session.match_id)
          and b.lifted_at is null and b.starts_at <= now() and (b.expires_at is null or b.expires_at > now())
    ) then
        raise exception 'USER_BANNED' using errcode = '42501';
    end if;
    update public.sessions set
        reconnect_token_hash = p_new_reconnect_token_hash,
        reconnect_expires_at = now() + interval '10 minutes',
        status = 'CONNECTED', last_seen_at = now(), updated_at = now()
    where id = v_session.id;
    update public.match_members set status = 'ACTIVE', last_seen_at = now(), disconnected_at = null
    where match_id = v_session.match_id and user_id = v_session.user_id and status = 'DISCONNECTED';
    return query select v_session.id, v_session.match_id, v_session.user_id, v_session.last_server_state;
end;
$$;

create or replace function public.renew_reconnect_ticket(
    p_user_id uuid,
    p_session_id uuid,
    p_old_reconnect_token_hash text,
    p_new_reconnect_token_hash text
)
returns timestamptz
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_session public.sessions;
    v_expiry timestamptz := now() + interval '10 minutes';
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_user_id is null or p_session_id is null or p_old_reconnect_token_hash is null or
       p_old_reconnect_token_hash !~ '^[0-9a-f]{64}$' or p_new_reconnect_token_hash is null or
       p_new_reconnect_token_hash !~ '^[0-9a-f]{64}$' then
        raise exception 'INVALID_TICKET' using errcode = '22023';
    end if;
    select * into v_session from public.sessions
    where id = p_session_id and user_id = p_user_id and status in ('CONNECTED', 'ELIMINATED')
      and reconnect_token_hash = p_old_reconnect_token_hash and reconnect_expires_at > now()
    for update;
    if not found then raise exception 'RECONNECT_UNAVAILABLE' using errcode = '42501'; end if;
    update public.sessions set reconnect_token_hash = p_new_reconnect_token_hash,
        reconnect_expires_at = v_expiry, last_seen_at = now(), updated_at = now()
    where id = p_session_id;
    return v_expiry;
end;
$$;

create or replace function public.server_mark_disconnected(
    p_session_id uuid,
    p_snapshot jsonb
)
returns boolean
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_session public.sessions;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_snapshot is not null and (jsonb_typeof(p_snapshot) <> 'object' or octet_length(p_snapshot::text) > 16384) then
        raise exception 'INVALID_SERVER_SNAPSHOT' using errcode = '22023';
    end if;
    select * into v_session from public.sessions
    where id = p_session_id and status in ('CONNECTED', 'ELIMINATED') for update;
    if not found then return false; end if;
    update public.sessions set status = 'DISCONNECTED', last_server_state = coalesce(p_snapshot, last_server_state),
        reconnect_expires_at = now() + interval '10 minutes', last_seen_at = now(), updated_at = now()
    where id = p_session_id;
    update public.match_members set status = 'DISCONNECTED', disconnected_at = now(), last_seen_at = now()
    where match_id = v_session.match_id and user_id = v_session.user_id and status = 'ACTIVE';
    return true;
end;
$$;

create or replace function public.server_mark_eliminated(
    p_session_id uuid,
    p_snapshot jsonb
)
returns boolean
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_session public.sessions;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_snapshot is not null and (jsonb_typeof(p_snapshot) <> 'object' or octet_length(p_snapshot::text) > 16384) then
        raise exception 'INVALID_SERVER_SNAPSHOT' using errcode = '22023';
    end if;
    select * into v_session from public.sessions
    where id = p_session_id and status in ('CONNECTED', 'DISCONNECTED', 'ELIMINATED') for update;
    if not found then return false; end if;
    if not exists (select 1 from public.matches where id = v_session.match_id and state = 'LIVE') then
        raise exception 'MATCH_NOT_LIVE' using errcode = '22023';
    end if;
    update public.sessions set status = 'ELIMINATED', last_server_state = coalesce(p_snapshot, last_server_state),
        reconnect_expires_at = now() + interval '10 minutes', last_seen_at = now(), updated_at = now()
    where id = p_session_id;
    update public.match_members set status = 'ELIMINATED', eliminated_at = coalesce(eliminated_at, now()),
        last_seen_at = now(), disconnected_at = null
    where match_id = v_session.match_id and user_id = v_session.user_id
      and status in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED');
    return true;
end;
$$;

create or replace function public.server_save_player_snapshots(
    p_match_id uuid,
    p_snapshots jsonb
)
returns integer
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_item jsonb;
    v_session_id uuid;
    v_state jsonb;
    v_saved integer := 0;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_match_id is null or p_snapshots is null or jsonb_typeof(p_snapshots) <> 'array' or jsonb_array_length(p_snapshots) > 32 then
        raise exception 'INVALID_SERVER_SNAPSHOTS' using errcode = '22023';
    end if;
    if not exists (select 1 from public.matches where id = p_match_id and state in ('LOADING', 'COUNTDOWN', 'LIVE')) then
        raise exception 'MATCH_NOT_ACTIVE' using errcode = '22023';
    end if;
    for v_item in select value from jsonb_array_elements(p_snapshots) loop
        v_session_id := (v_item ->> 'session_id')::uuid;
        v_state := v_item -> 'player_state';
        if v_state is null or jsonb_typeof(v_state) <> 'object' or octet_length(v_state::text) > 16384 then
            raise exception 'INVALID_SERVER_SNAPSHOT' using errcode = '22023';
        end if;
        update public.sessions set last_server_state = v_state, last_seen_at = now(), updated_at = now()
        where id = v_session_id and match_id = p_match_id
          and status in ('CONNECTED', 'DISCONNECTED', 'ELIMINATED');
        if found then
            update public.match_members mm set last_seen_at = now()
            where mm.match_id = p_match_id and mm.user_id = (
                select s.user_id from public.sessions s where s.id = v_session_id
            );
            v_saved := v_saved + 1;
        end if;
    end loop;
    return v_saved;
end;
$$;

create or replace function public.host_match_action(
    p_host_user_id uuid,
    p_match_id uuid,
    p_action text,
    p_target_user_id uuid default null,
    p_new_maximum_players integer default null
)
returns jsonb
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_match public.matches;
    v_active_count integer;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    select * into v_match from public.matches where id = p_match_id for update;
    if not found then raise exception 'MATCH_NOT_FOUND' using errcode = 'P0002'; end if;
    if v_match.host_user_id <> p_host_user_id then raise exception 'HOST_REQUIRED' using errcode = '42501'; end if;

    if p_action = 'LOCK_LOBBY' then
        if v_match.state <> 'LOBBY' then raise exception 'LOBBY_ONLY' using errcode = '22023'; end if;
        update public.matches set is_locked = true, updated_at = now() where id = p_match_id;
    elsif p_action = 'UNLOCK_LOBBY' then
        if v_match.state <> 'LOBBY' then raise exception 'LOBBY_ONLY' using errcode = '22023'; end if;
        update public.matches set is_locked = false, updated_at = now() where id = p_match_id;
    elsif p_action = 'SET_MAX_PLAYERS' then
        if v_match.state <> 'LOBBY' or p_new_maximum_players not between 2 and 32 then
            raise exception 'INVALID_PLAYER_LIMIT' using errcode = '22023';
        end if;
        select count(*) into v_active_count from public.match_members
        where match_id = p_match_id and status in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED');
        if p_new_maximum_players < v_active_count then
            raise exception 'PLAYER_LIMIT_BELOW_CURRENT_COUNT' using errcode = '23514';
        end if;
        update public.matches set maximum_players = p_new_maximum_players, updated_at = now() where id = p_match_id;
    elsif p_action = 'KICK' then
        if p_target_user_id is null or p_target_user_id = p_host_user_id then
            raise exception 'INVALID_KICK_TARGET' using errcode = '22023';
        end if;
        update public.match_members set status = 'KICKED', kicked_at = now()
        where match_id = p_match_id and user_id = p_target_user_id
          and status in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED');
        if not found then raise exception 'PLAYER_NOT_KICKABLE' using errcode = 'P0002'; end if;
        update public.sessions set status = 'CLOSED', reconnect_token_hash = null,
            reconnect_expires_at = null, updated_at = now()
        where match_id = p_match_id and user_id = p_target_user_id and status <> 'CLOSED';
    elsif p_action = 'END_MATCH' then
        if v_match.state not in ('LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE') then
            raise exception 'MATCH_NOT_ENDABLE' using errcode = '22023';
        end if;
        update public.matches set end_requested_at = coalesce(end_requested_at, now()), updated_at = now()
        where id = p_match_id;
    else
        raise exception 'UNKNOWN_HOST_ACTION' using errcode = '22023';
    end if;

    insert into public.match_events (match_id, actor_user_id, event_type, event_data)
    values (p_match_id, p_host_user_id, 'HOST_ACTION', jsonb_build_object(
        'action', p_action, 'target_user_id', p_target_user_id, 'maximum_players', p_new_maximum_players
    ));
    return jsonb_build_object('match_id', p_match_id, 'action', p_action, 'accepted', true);
end;
$$;

create or replace function public.record_match_results(
    p_match_id uuid,
    p_results jsonb
)
returns integer
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_match public.matches;
    v_item jsonb;
    v_user_id uuid;
    v_kills integer;
    v_deaths integer;
    v_placement integer;
    v_won boolean;
    v_score integer;
    v_inserted_user_id uuid;
    v_saved integer := 0;
    v_count integer;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_match_id is null or p_results is null or jsonb_typeof(p_results) <> 'array' or
       jsonb_array_length(p_results) < 1 or jsonb_array_length(p_results) > 32 then
        raise exception 'INVALID_RESULTS' using errcode = '22023';
    end if;
    select * into v_match from public.matches where id = p_match_id for update;
    if not found or v_match.state <> 'LIVE' then raise exception 'MATCH_NOT_LIVE' using errcode = '22023'; end if;
    select count(distinct value ->> 'user_id') into v_count from jsonb_array_elements(p_results);
    if v_count <> jsonb_array_length(p_results) then raise exception 'DUPLICATE_RESULT_USER' using errcode = '22023'; end if;
    if (select count(*) from jsonb_array_elements(p_results) where coalesce((value ->> 'won')::boolean, false)) > 1 then
        raise exception 'MULTIPLE_WINNERS' using errcode = '22023';
    end if;
    select count(*) into v_count from public.match_members
    where match_id = p_match_id and status in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED');
    if v_count <> jsonb_array_length(p_results) then
        raise exception 'INCOMPLETE_MATCH_RESULTS' using errcode = '22023';
    end if;

    for v_item in select value from jsonb_array_elements(p_results) loop
        v_user_id := (v_item ->> 'user_id')::uuid;
        v_kills := (v_item ->> 'kills')::integer;
        v_deaths := (v_item ->> 'deaths')::integer;
        v_placement := (v_item ->> 'placement')::integer;
        v_won := coalesce((v_item ->> 'won')::boolean, false);
        v_score := coalesce((v_item ->> 'score')::integer, 0);
        if v_kills not between 0 and 10000 or v_deaths not between 0 and 10000 or
           v_placement not between 1 and v_match.maximum_players or
           v_score not between -1000000 and 1000000 or
           not exists (select 1 from public.match_members mm where mm.match_id = p_match_id and mm.user_id = v_user_id
               and mm.status in ('ACTIVE', 'DISCONNECTED', 'ELIMINATED')) then
            raise exception 'INVALID_PLAYER_RESULT' using errcode = '22023';
        end if;
        insert into public.match_results (match_id, user_id, kills, deaths, placement, won, score)
        values (p_match_id, v_user_id, v_kills, v_deaths, v_placement, v_won, v_score)
        on conflict (match_id, user_id) do nothing
        returning user_id into v_inserted_user_id;
        if v_inserted_user_id is not null then
            insert into public.statistics (user_id, matches_played, kills, deaths, wins, updated_at)
            values (v_user_id, 1, v_kills, v_deaths, case when v_won then 1 else 0 end, now())
            on conflict (user_id) do update set
                matches_played = public.statistics.matches_played + 1,
                kills = public.statistics.kills + excluded.kills,
                deaths = public.statistics.deaths + excluded.deaths,
                wins = public.statistics.wins + excluded.wins,
                updated_at = now();
            v_saved := v_saved + 1;
        end if;
    end loop;

    update public.matches set state = 'FINISHED', finished_at = now(), updated_at = now(),
        state_version = state_version + 1 where id = p_match_id;
    insert into public.match_events (match_id, event_type, event_data)
    values (p_match_id, 'STATE_CHANGED', jsonb_build_object('to', 'FINISHED', 'results', v_saved));
    return v_saved;
end;
$$;

create or replace function public.close_stale_matches(
    p_server_stale_seconds integer default 90,
    p_creating_stale_seconds integer default 300,
    p_finished_stale_seconds integer default 3600
)
returns table (match_id uuid, previous_state text)
language plpgsql
security definer
set search_path = pg_catalog, public, auth, pg_temp
as $$
declare
    v_now timestamptz := now();
    v_match public.matches;
    v_closed integer := 0;
begin
    if coalesce(auth.role(), '') <> 'service_role' then
        raise exception 'FORBIDDEN' using errcode = '42501';
    end if;
    if p_server_stale_seconds is null or p_server_stale_seconds < 30 or p_server_stale_seconds > 3600 or
       p_creating_stale_seconds is null or p_creating_stale_seconds < 60 or p_creating_stale_seconds > 3600 or
       p_finished_stale_seconds is null or p_finished_stale_seconds < 60 or p_finished_stale_seconds > 86400 then
        raise exception 'INVALID_RECONCILE_WINDOW' using errcode = '22023';
    end if;

    for v_match in
        select m.* from public.matches m
        where (m.state = 'CREATING' and m.created_at < v_now - (p_creating_stale_seconds * interval '1 second'))
           or (m.state in ('LOBBY', 'LOADING', 'COUNTDOWN', 'LIVE') and
               coalesce(m.server_heartbeat_at, m.updated_at) < v_now - (p_server_stale_seconds * interval '1 second'))
           or (m.state = 'FINISHED' and m.finished_at < v_now - (p_finished_stale_seconds * interval '1 second'))
        order by m.updated_at asc
        limit 100
        for update skip locked
    loop
        update public.matches set state = 'CLOSED', closed_at = v_now,
            updated_at = v_now, state_version = state_version + 1
        where id = v_match.id;
        update public.sessions set status = 'CLOSED', connection_ticket_hash = null,
            connection_ticket_expires_at = null, reconnect_token_hash = null,
            reconnect_expires_at = null, updated_at = v_now
        where sessions.match_id = v_match.id and status <> 'CLOSED';
        insert into public.match_events (match_id, event_type, event_data)
        values (v_match.id, 'STALE_MATCH_CLOSED', jsonb_build_object('previous_state', v_match.state));
        match_id := v_match.id;
        previous_state := v_match.state;
        return next;
        v_closed := v_closed + 1;
    end loop;
    return;
end;
$$;

-- All game data is accessed through the trusted Vercel API. Do not expose direct table writes to clients.
do $$
declare
    t text;
begin
    foreach t in array array[
        'users', 'statistics', 'matches', 'match_members', 'match_results', 'sessions',
        'reports', 'bans', 'user_blocks', 'user_mutes', 'chat_messages', 'match_events'
    ] loop
        execute format('alter table public.%I enable row level security', t);
        execute format('revoke all on table public.%I from anon, authenticated', t);
        execute format('grant all on table public.%I to service_role', t);
    end loop;
end;
$$;

revoke all on function public.consume_rate_limit(text, text, integer, integer) from public, anon, authenticated;
revoke all on function public.create_match(uuid, double precision, double precision, double precision, double precision, integer, text) from public, anon, authenticated;
revoke all on function public.transition_match(uuid, text) from public, anon, authenticated;
revoke all on function public.join_match_by_code(uuid, text, text, text) from public, anon, authenticated;
revoke all on function public.consume_connection_ticket(text) from public, anon, authenticated;
revoke all on function public.redeem_reconnect_ticket(text, text) from public, anon, authenticated;
revoke all on function public.renew_reconnect_ticket(uuid, uuid, text, text) from public, anon, authenticated;
revoke all on function public.server_mark_disconnected(uuid, jsonb) from public, anon, authenticated;
revoke all on function public.server_mark_eliminated(uuid, jsonb) from public, anon, authenticated;
revoke all on function public.server_save_player_snapshots(uuid, jsonb) from public, anon, authenticated;
revoke all on function public.host_match_action(uuid, uuid, text, uuid, integer) from public, anon, authenticated;
revoke all on function public.record_match_results(uuid, jsonb) from public, anon, authenticated;
revoke all on function public.close_stale_matches(integer, integer, integer) from public, anon, authenticated;
grant execute on function public.consume_rate_limit(text, text, integer, integer) to service_role;
grant execute on function public.create_match(uuid, double precision, double precision, double precision, double precision, integer, text) to service_role;
grant execute on function public.transition_match(uuid, text) to service_role;
grant execute on function public.join_match_by_code(uuid, text, text, text) to service_role;
grant execute on function public.consume_connection_ticket(text) to service_role;
grant execute on function public.redeem_reconnect_ticket(text, text) to service_role;
grant execute on function public.renew_reconnect_ticket(uuid, uuid, text, text) to service_role;
grant execute on function public.server_mark_disconnected(uuid, jsonb) to service_role;
grant execute on function public.server_mark_eliminated(uuid, jsonb) to service_role;
grant execute on function public.server_save_player_snapshots(uuid, jsonb) to service_role;
grant execute on function public.host_match_action(uuid, uuid, text, uuid, integer) to service_role;
grant execute on function public.record_match_results(uuid, jsonb) to service_role;
grant execute on function public.close_stale_matches(integer, integer, integer) to service_role;

commit;
