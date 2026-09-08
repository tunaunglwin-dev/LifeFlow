create table if not exists lifeflow_users (
  id serial primary key,
  name text not null unique,
  password_hash text not null,
  created_at timestamptz not null default now()
);

create table if not exists donors (
  id serial primary key,
  name text not null,
  age integer not null check (age between 1 and 120),
  phone text not null,
  gender text not null,
  blood_type text not null check (blood_type in ('A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-')),
  address text not null default '',
  created_at timestamptz not null default now()
);

create table if not exists blood_stock (
  id serial primary key,
  donor_name text not null,
  blood_type text not null check (blood_type in ('A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-')),
  units integer not null check (units >= 0),
  status text not null,
  collected_at date not null default current_date,
  expires_at date not null default (current_date + interval '90 days')
);

create table if not exists blood_requests (
  id serial primary key,
  patient_name text not null,
  hospital_name text not null,
  blood_type text not null check (blood_type in ('A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-')),
  units integer not null check (units > 0),
  status text not null default 'Pending',
  created_at timestamptz not null default now()
);

create table if not exists blood_transfers (
  id serial primary key,
  patient_name text not null,
  hospital_name text not null,
  blood_type text not null check (blood_type in ('A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-')),
  units integer not null check (units > 0),
  transferred_at timestamptz not null default now()
);
