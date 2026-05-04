create extension if not exists "uuid-ossp";
create extension if not exists "btree_gist";

create table if not exists integration_outbox (
    id uuid primary key,
    event_id uuid not null,
    event_name varchar(160) not null,
    routing_key varchar(160) not null,
    payload jsonb not null,
    occurred_at timestamp with time zone not null,
    created_at timestamp with time zone not null default now(),
    processed_at timestamp with time zone null,
    error text null
);

create table if not exists processed_integration_events (
    handler varchar(160) not null,
    event_id uuid not null,
    processed_at timestamp with time zone not null default now(),
    primary key (handler, event_id)
);

create table if not exists roles (
    id uuid primary key,
    name varchar(120) not null,
    code varchar(80) not null unique
);

create table if not exists permissions (
    id uuid primary key,
    code varchar(120) not null unique,
    description text
);

create table if not exists role_permissions (
    role_id uuid not null references roles(id) on delete cascade,
    permission_id uuid not null references permissions(id) on delete cascade,
    primary key (role_id, permission_id)
);

create table if not exists branches (
    id uuid primary key,
    name varchar(160) not null,
    address text not null,
    phone varchar(40) not null,
    timezone varchar(80) not null,
    is_active boolean not null default true,
    created_at timestamp with time zone not null default now()
);

create table if not exists users (
    id uuid primary key,
    role_id uuid not null references roles(id),
    branch_id uuid null references branches(id),
    name varchar(160) not null,
    phone varchar(40) not null unique,
    email varchar(160) null,
    password_hash varchar(512) not null,
    status varchar(40) not null,
    created_at timestamp with time zone not null default now(),
    updated_at timestamp with time zone not null default now()
);

create table if not exists refresh_tokens (
    id uuid primary key,
    user_id uuid not null references users(id) on delete cascade,
    token_hash varchar(128) not null unique,
    created_at timestamp with time zone not null default now(),
    expires_at timestamp with time zone not null,
    revoked_at timestamp with time zone null
);

create table if not exists staff_shifts (
    id uuid primary key,
    staff_id uuid not null references users(id),
    branch_id uuid not null references branches(id),
    starts_at timestamp with time zone not null,
    ends_at timestamp with time zone not null,
    status varchar(40) not null,
    actual_started_at timestamp with time zone null,
    actual_finished_at timestamp with time zone null,
    role_on_shift varchar(80),
    cancel_reason text,
    check (starts_at < ends_at)
);

create table if not exists halls (
    id uuid primary key,
    branch_id uuid not null references branches(id) on delete cascade,
    name varchar(160) not null,
    description text
);

create table if not exists zones (
    id uuid primary key,
    branch_id uuid not null references branches(id) on delete cascade,
    name varchar(160) not null,
    description text,
    color varchar(32),
    x_position numeric(10, 2) not null default 40,
    y_position numeric(10, 2) not null default 40,
    width numeric(10, 2) not null default 360,
    height numeric(10, 2) not null default 220,
    is_active boolean not null default true
);

create table if not exists branch_working_hours (
    branch_id uuid not null references branches(id) on delete cascade,
    day_of_week int not null check (day_of_week between 0 and 6),
    opens_at time not null,
    closes_at time not null,
    is_closed boolean not null default false,
    primary key (branch_id, day_of_week)
);

create table if not exists tables (
    id uuid primary key,
    hall_id uuid not null references halls(id) on delete cascade,
    zone_id uuid null references zones(id),
    name varchar(80) not null,
    capacity int not null check (capacity > 0),
    status varchar(40) not null,
    x_position numeric(10, 2) not null,
    y_position numeric(10, 2) not null,
    is_active boolean not null default true
);

create table if not exists hookahs (
    id uuid primary key,
    branch_id uuid not null references branches(id),
    name varchar(160) not null,
    brand varchar(120) not null,
    model varchar(120) not null,
    status varchar(40) not null,
    photo_url text,
    last_service_at timestamp with time zone
);

create table if not exists bowls (
    id uuid primary key,
    name varchar(160) not null,
    type varchar(80) not null,
    capacity_grams numeric(8, 2) not null check (capacity_grams > 0),
    recommended_strength varchar(40) not null,
    average_smoke_minutes int not null check (average_smoke_minutes > 0),
    is_active boolean not null default true
);

create table if not exists tobaccos (
    id uuid primary key,
    brand varchar(120) not null,
    line varchar(120) not null,
    flavor varchar(160) not null,
    strength varchar(40) not null,
    category varchar(80) not null,
    description text,
    cost_per_gram numeric(10, 2) not null check (cost_per_gram >= 0),
    is_active boolean not null default true,
    photo_url text
);

create table if not exists inventory_items (
    id uuid primary key,
    branch_id uuid not null references branches(id),
    tobacco_id uuid not null references tobaccos(id),
    stock_grams numeric(12, 2) not null default 0 check (stock_grams >= 0),
    min_stock_grams numeric(12, 2) not null default 0 check (min_stock_grams >= 0),
    updated_at timestamp with time zone not null default now(),
    unique (branch_id, tobacco_id)
);

create table if not exists mixes (
    id uuid primary key,
    name varchar(160) not null,
    description text,
    bowl_id uuid not null references bowls(id),
    strength varchar(40) not null,
    taste_profile varchar(80) not null,
    total_grams numeric(8, 2) not null check (total_grams > 0),
    price numeric(12, 2) not null check (price >= 0),
    cost numeric(12, 2) not null check (cost >= 0),
    margin numeric(12, 2) not null,
    is_public boolean not null default false,
    is_active boolean not null default true,
    created_by uuid null references users(id),
    created_at timestamp with time zone not null default now()
);

create table if not exists mix_items (
    id uuid primary key,
    mix_id uuid not null references mixes(id) on delete cascade,
    tobacco_id uuid not null references tobaccos(id),
    percent numeric(5, 2) not null check (percent > 0 and percent <= 100),
    grams numeric(8, 2) not null check (grams > 0)
);

create table if not exists bookings (
    id uuid primary key,
    client_id uuid not null references users(id),
    branch_id uuid not null references branches(id),
    table_id uuid not null references tables(id),
    hookah_id uuid null references hookahs(id),
    bowl_id uuid null references bowls(id),
    mix_id uuid null references mixes(id),
    start_time timestamp with time zone not null,
    end_time timestamp with time zone not null,
    guests_count int not null check (guests_count > 0),
    status varchar(40) not null,
    deposit_amount numeric(12, 2) not null default 0 check (deposit_amount >= 0),
    payment_id uuid null,
    deposit_paid_at timestamp with time zone null,
    comment text,
    created_at timestamp with time zone not null default now(),
    check (start_time < end_time)
);

create table if not exists orders (
    id uuid primary key,
    branch_id uuid not null references branches(id),
    table_id uuid not null references tables(id),
    client_id uuid null references users(id),
    hookah_master_id uuid null references users(id),
    waiter_id uuid null references users(id),
    booking_id uuid null references bookings(id),
    status varchar(40) not null,
    total_price numeric(12, 2) not null default 0 check (total_price >= 0),
    comment text,
    created_at timestamp with time zone not null default now(),
    served_at timestamp with time zone null,
    completed_at timestamp with time zone null,
    inventory_written_off_at timestamp with time zone null,
    payment_id uuid null,
    paid_amount numeric(12, 2) not null default 0 check (paid_amount >= 0),
    paid_at timestamp with time zone null
);

create table if not exists order_items (
    id uuid primary key,
    order_id uuid not null references orders(id) on delete cascade,
    hookah_id uuid not null references hookahs(id),
    bowl_id uuid not null references bowls(id),
    mix_id uuid not null references mixes(id),
    price numeric(12, 2) not null check (price >= 0),
    status varchar(40) not null
);

create table if not exists coal_changes (
    id uuid primary key,
    order_id uuid not null references orders(id) on delete cascade,
    changed_at timestamp with time zone not null
);

create table if not exists inventory_movements (
    id uuid primary key,
    branch_id uuid not null references branches(id),
    tobacco_id uuid not null references tobaccos(id),
    type varchar(40) not null,
    amount_grams numeric(12, 2) not null,
    reason varchar(240),
    order_id uuid null references orders(id),
    created_by uuid null references users(id),
    created_at timestamp with time zone not null default now()
);

create table if not exists payments (
    id uuid primary key,
    client_id uuid not null references users(id),
    order_id uuid null references orders(id),
    booking_id uuid null references bookings(id),
    original_amount numeric(12, 2) not null check (original_amount > 0),
    discount_amount numeric(12, 2) not null default 0 check (discount_amount >= 0),
    payable_amount numeric(12, 2) not null check (payable_amount >= 0),
    refunded_amount numeric(12, 2) not null default 0 check (refunded_amount >= 0),
    currency varchar(8) not null,
    provider varchar(80) not null,
    promocode varchar(80) null,
    external_payment_id varchar(160),
    status varchar(40) not null,
    type varchar(40) not null,
    created_at timestamp with time zone not null default now()
);

create unique index if not exists ux_payments_external_payment_id
    on payments(external_payment_id)
    where external_payment_id is not null;

create table if not exists notification_templates (
    code varchar(120) primary key,
    channel varchar(40) not null,
    title varchar(200) not null,
    message text not null
);

create table if not exists notification_preferences (
    user_id uuid primary key references users(id) on delete cascade,
    crm_enabled boolean not null default true,
    telegram_enabled boolean not null default true,
    sms_enabled boolean not null default true,
    email_enabled boolean not null default true,
    push_enabled boolean not null default true
);

create table if not exists notifications (
    id uuid primary key,
    user_id uuid not null references users(id) on delete cascade,
    channel varchar(40) not null,
    title varchar(200) not null,
    message text not null,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamp with time zone not null default now(),
    read_at timestamp with time zone null
);

do $$
begin
    if not exists (
        select 1 from information_schema.table_constraints
        where constraint_name = 'fk_bookings_payment_id'
          and table_name = 'bookings'
    ) then
        alter table bookings
            add constraint fk_bookings_payment_id foreign key (payment_id) references payments(id);
    end if;
end $$;

do $$
begin
    if not exists (
        select 1 from information_schema.table_constraints
        where constraint_name = 'fk_orders_payment_id'
          and table_name = 'orders'
    ) then
        alter table orders
            add constraint fk_orders_payment_id foreign key (payment_id) references payments(id);
    end if;
end $$;

create table if not exists reviews (
    id uuid primary key,
    client_id uuid not null references users(id),
    mix_id uuid null references mixes(id),
    order_id uuid null references orders(id),
    rating int not null check (rating between 1 and 5),
    text text,
    created_at timestamp with time zone not null default now()
);

create table if not exists promocodes (
    id uuid primary key,
    code varchar(80) not null unique,
    discount_type varchar(40) not null,
    discount_value numeric(12, 2) not null check (discount_value > 0),
    valid_from date not null,
    valid_to date not null,
    max_redemptions int null check (max_redemptions is null or max_redemptions > 0),
    per_client_limit int not null default 1 check (per_client_limit > 0),
    is_active boolean not null default true,
    check (valid_from <= valid_to)
);

create table if not exists promocode_redemptions (
    id uuid primary key,
    code varchar(80) not null references promocodes(code),
    client_id uuid not null references users(id),
    order_id uuid null references orders(id),
    order_amount numeric(12, 2) not null check (order_amount >= 0),
    discount_amount numeric(12, 2) not null check (discount_amount >= 0),
    created_at timestamp with time zone not null default now()
);

create table if not exists analytics_orders (
    id uuid primary key,
    branch_id uuid not null,
    table_id uuid not null,
    mix_id uuid not null,
    hookah_master_id uuid null,
    total_price numeric(12, 2) not null default 0,
    status varchar(40) not null,
    created_at timestamp with time zone not null
);

create table if not exists analytics_bookings (
    id uuid primary key,
    branch_id uuid not null,
    table_id uuid not null,
    status varchar(40) not null,
    start_time timestamp with time zone null,
    end_time timestamp with time zone null,
    created_at timestamp with time zone not null
);

create table if not exists analytics_tobacco_usage (
    branch_id uuid not null,
    tobacco_id uuid not null,
    grams numeric(12, 2) not null default 0,
    primary key (branch_id, tobacco_id)
);

create index if not exists ix_bookings_table_time on bookings(table_id, start_time, end_time);
create index if not exists ix_staff_shifts_staff_time on staff_shifts(staff_id, starts_at, ends_at);
create index if not exists ix_orders_branch_status on orders(branch_id, status);
create index if not exists ix_coal_changes_order_changed_at on coal_changes(order_id, changed_at desc);
create index if not exists ix_inventory_items_low_stock on inventory_items(branch_id, stock_grams, min_stock_grams);
create index if not exists ix_inventory_movements_branch_created_at on inventory_movements(branch_id, created_at);
create index if not exists ix_notifications_user_created_at on notifications(user_id, created_at desc);
create index if not exists ix_promocode_redemptions_code_client on promocode_redemptions(code, client_id);
create index if not exists ix_analytics_orders_branch_created_at on analytics_orders(branch_id, created_at);
create index if not exists ix_analytics_bookings_branch_created_at on analytics_bookings(branch_id, created_at);
create index if not exists ix_integration_outbox_pending on integration_outbox(processed_at, created_at);
create index if not exists ix_refresh_tokens_user_active on refresh_tokens(user_id, revoked_at, expires_at);
create index if not exists ix_processed_integration_events_processed_at on processed_integration_events(processed_at);

do $$
begin
    if not exists (
        select 1 from pg_constraint
        where conname = 'ex_bookings_no_table_overlap'
    ) then
        alter table bookings
            add constraint ex_bookings_no_table_overlap
            exclude using gist (
                table_id with =,
                tstzrange(start_time, end_time, '[)') with &&
            )
            where (status not in ('CANCELLED', 'NO_SHOW'));
    end if;
end $$;

create or replace function ensure_mix_items_percent_sum()
returns trigger
language plpgsql
as $$
declare
    affected_mix_id uuid;
    percent_sum numeric(8, 2);
begin
    affected_mix_id := coalesce(new.mix_id, old.mix_id);

    select coalesce(sum(percent), 0)
    into percent_sum
    from mix_items
    where mix_id = affected_mix_id;

    if percent_sum <> 100 then
        raise exception 'mix_items percent sum for mix % must be 100, actual %', affected_mix_id, percent_sum;
    end if;

    return null;
end;
$$;

drop trigger if exists trg_mix_items_percent_sum_insert on mix_items;
drop trigger if exists trg_mix_items_percent_sum_update on mix_items;
drop trigger if exists trg_mix_items_percent_sum_delete on mix_items;

create constraint trigger trg_mix_items_percent_sum_insert
after insert on mix_items
deferrable initially deferred
for each row execute function ensure_mix_items_percent_sum();

create constraint trigger trg_mix_items_percent_sum_update
after update on mix_items
deferrable initially deferred
for each row execute function ensure_mix_items_percent_sum();

create constraint trigger trg_mix_items_percent_sum_delete
after delete on mix_items
deferrable initially deferred
for each row execute function ensure_mix_items_percent_sum();

insert into roles(id, name, code) values
    ('01000000-0000-0000-0000-000000000001', 'Owner', 'OWNER'),
    ('01000000-0000-0000-0000-000000000002', 'Manager', 'MANAGER'),
    ('01000000-0000-0000-0000-000000000003', 'Hookah master', 'HOOKAH_MASTER'),
    ('01000000-0000-0000-0000-000000000004', 'Waiter', 'WAITER'),
    ('01000000-0000-0000-0000-000000000005', 'Client', 'CLIENT')
on conflict (code) do nothing;

insert into permissions(id, code, description) values
    ('02000000-0000-0000-0000-000000000001', 'branches.manage', 'Manage branches, halls and tables'),
    ('02000000-0000-0000-0000-000000000002', 'staff.manage', 'Manage staff accounts'),
    ('02000000-0000-0000-0000-000000000003', 'mixes.manage', 'Manage bowls, tobaccos and mixes'),
    ('02000000-0000-0000-0000-000000000004', 'inventory.manage', 'Manage stock and inventory movements'),
    ('02000000-0000-0000-0000-000000000005', 'orders.manage', 'Manage hookah orders'),
    ('02000000-0000-0000-0000-000000000006', 'bookings.manage', 'Manage bookings'),
    ('02000000-0000-0000-0000-000000000007', 'analytics.read', 'Read analytics dashboards'),
    ('02000000-0000-0000-0000-000000000008', 'bookings.create', 'Create client bookings')
on conflict (code) do nothing;

insert into role_permissions(role_id, permission_id)
select r.id, p.id
from roles r
cross join permissions p
where r.code = 'OWNER'
on conflict do nothing;

insert into role_permissions(role_id, permission_id)
select r.id, p.id
from roles r
join permissions p on p.code = any(array[
    'branches.manage',
    'staff.manage',
    'mixes.manage',
    'inventory.manage',
    'orders.manage',
    'bookings.manage',
    'analytics.read'
])
where r.code = 'MANAGER'
on conflict do nothing;

insert into role_permissions(role_id, permission_id)
select r.id, p.id
from roles r
join permissions p on p.code = any(array[
    'mixes.manage',
    'inventory.manage',
    'orders.manage'
])
where r.code = 'HOOKAH_MASTER'
on conflict do nothing;

insert into role_permissions(role_id, permission_id)
select r.id, p.id
from roles r
join permissions p on p.code = any(array[
    'orders.manage',
    'bookings.manage'
])
where r.code = 'WAITER'
on conflict do nothing;

insert into role_permissions(role_id, permission_id)
select r.id, p.id
from roles r
join permissions p on p.code = 'bookings.create'
where r.code = 'CLIENT'
on conflict do nothing;

insert into branches(id, name, address, phone, timezone) values
    ('10000000-0000-0000-0000-000000000001', 'Hookah Place Center', 'Lenina, 1', '+79990000000', 'Europe/Moscow')
on conflict (id) do nothing;

insert into users(id, role_id, branch_id, name, phone, email, password_hash, status) values
    ('90000000-0000-0000-0000-000000000000', '01000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Owner', '+79990000000', 'owner@hookah.local', 'PBKDF2-SHA256$100000$aG9va2FoLXNlZWQtc2FsdCE=$skse35EKG1yHQWKX2a/9h4UKDo6zFXk6XVJSybEXVJM=', 'active'),
    ('90000000-0000-0000-0000-000000000001', '01000000-0000-0000-0000-000000000005', null, 'Client', '+79990000001', 'client@hookah.local', 'PBKDF2-SHA256$100000$aG9va2FoLXNlZWQtc2FsdCE=$skse35EKG1yHQWKX2a/9h4UKDo6zFXk6XVJSybEXVJM=', 'active'),
    ('90000000-0000-0000-0000-000000000010', '01000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', 'Hookah Master', '+79991112233', null, 'PBKDF2-SHA256$100000$aG9va2FoLXNlZWQtc2FsdCE=$skse35EKG1yHQWKX2a/9h4UKDo6zFXk6XVJSybEXVJM=', 'active')
on conflict (id) do nothing;

insert into branch_working_hours(branch_id, day_of_week, opens_at, closes_at, is_closed)
select '10000000-0000-0000-0000-000000000001', day, time '12:00', time '02:00', false
from generate_series(0, 6) as day
on conflict (branch_id, day_of_week) do nothing;

insert into halls(id, branch_id, name, description) values
    ('20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Main hall', 'First floor')
on conflict (id) do nothing;

insert into zones(id, branch_id, name, description, color, x_position, y_position, width, height) values
    ('21000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Main zone', 'Central seating area', '#2f7d6d', 30, 40, 460, 280)
on conflict (id) do nothing;

insert into tables(id, hall_id, zone_id, name, capacity, status, x_position, y_position) values
    ('30000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '21000000-0000-0000-0000-000000000001', 'Table 1', 4, 'FREE', 120, 300),
    ('30000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000001', '21000000-0000-0000-0000-000000000001', 'Table 2', 6, 'FREE', 260, 300)
on conflict (id) do nothing;

insert into hookahs(id, branch_id, name, brand, model, status) values
    ('40000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Alpha X', 'Alpha Hookah', 'X', 'AVAILABLE')
on conflict (id) do nothing;

insert into bowls(id, name, type, capacity_grams, recommended_strength, average_smoke_minutes) values
    ('50000000-0000-0000-0000-000000000001', 'Oblako Phunnel M', 'PHUNNEL', 18, 'MEDIUM', 70)
on conflict (id) do nothing;

insert into tobaccos(id, brand, line, flavor, strength, category, description, cost_per_gram) values
    ('60000000-0000-0000-0000-000000000001', 'Darkside', 'Base', 'Strawberry', 'STRONG', 'BERRY', 'Strawberry flavor', 8.5),
    ('60000000-0000-0000-0000-000000000002', 'Musthave', 'Classic', 'Mint', 'MEDIUM', 'FRESH', 'Cooling mint', 6.8),
    ('60000000-0000-0000-0000-000000000003', 'Element', 'Air', 'Blueberry', 'LIGHT', 'BERRY', 'Blueberry flavor', 7.2)
on conflict (id) do nothing;

insert into inventory_items(id, branch_id, tobacco_id, stock_grams, min_stock_grams) values
    ('61000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001', 250, 50),
    ('61000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000002', 250, 50),
    ('61000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000003', 250, 50)
on conflict (branch_id, tobacco_id) do nothing;

insert into mixes(id, name, description, bowl_id, strength, taste_profile, total_grams, price, cost, margin, is_public, is_active, created_by) values
    ('70000000-0000-0000-0000-000000000001', 'Berry Ice', 'Berry and fresh medium mix', '50000000-0000-0000-0000-000000000001', 'MEDIUM', 'BERRY_FRESH', 18, 850, 125.82, 724.18, true, true, '90000000-0000-0000-0000-000000000010')
on conflict (id) do nothing;

insert into mix_items(id, mix_id, tobacco_id, percent, grams) values
    ('71000000-0000-0000-0000-000000000001', '70000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001', 40, 7.2),
    ('71000000-0000-0000-0000-000000000002', '70000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000002', 30, 5.4),
    ('71000000-0000-0000-0000-000000000003', '70000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000003', 30, 5.4)
on conflict (id) do nothing;

insert into promocodes(id, code, discount_type, discount_value, valid_from, valid_to, max_redemptions, per_client_limit) values
    ('a0000000-0000-0000-0000-000000000001', 'HOOKAH20', 'PERCENT', 20, date '2026-05-01', date '2026-06-01', 500, 1)
on conflict (code) do nothing;

insert into notification_templates(code, channel, title, message) values
    ('booking.created.manager', 'CRM', 'Новая бронь', 'Стол {tableId} забронирован на {startTime}.'),
    ('booking.confirmed.client', 'PUSH', 'Бронь подтверждена', 'Ждем вас {startTime}.'),
    ('booking.cancelled.manager', 'CRM', 'Бронь отменена', 'Бронь {bookingId} отменена.'),
    ('payment.succeeded.client', 'PUSH', 'Оплата прошла', 'Депозит {amount} ₽ получен.'),
    ('payment.failed.manager', 'CRM', 'Платеж не прошел', 'Платеж {paymentId} отклонен: {reason}.'),
    ('payment.refunded.client', 'PUSH', 'Возврат оформлен', 'Возвращено {amount} ₽.'),
    ('inventory.low-stock.manager', 'CRM', 'Низкий остаток', 'Табак {tobaccoId}: осталось {stockGrams} г.'),
    ('order.served.coal-timer', 'CRM', 'Кальян вынесен', 'Запущен таймер углей по заказу {orderId}.')
on conflict (code) do nothing;
