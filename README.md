# App Platform

AWS CDK (C#) that creates and destroys shared **dev** and **test** environments for ConstFlow, ProFlow, and WaterFlow. Each environment has a shared VPC, Application Load Balancer, and ECS cluster. Each app gets:

- React static site on S3 + CloudFront
- ASP.NET API on ECS Fargate
- PostgreSQL on RDS

This repository is **platform only**. Application source, Dockerfiles, and React builds live in the ConstFlow, ProFlow, and WaterFlow repos.

## Prerequisites

- .NET 8 SDK
- Node.js 22 (for the CDK CLI)
- AWS CLI configured for the target account
- A current AWS CDK CLI (2.1000+). Either `npm install` in this repo and use `npx cdk`, or `npm i -g aws-cdk@2`

Then install the local CLI once:

```powershell
npm install
```

Bootstrap the account/region once:

```powershell
npx cdk bootstrap aws://<account-id>/us-west-1
```

Default region is `us-west-1` (override with `CDK_DEFAULT_REGION` or your AWS CLI profile).

## Commands

```powershell
# Create or update an environment (all apps)
.\scripts\deploy-env.ps1 -Env dev
.\scripts\deploy-env.ps1 -Env test

# Create or update one app (shared VPC/ALB/cluster is deployed if needed)
.\scripts\deploy-env.ps1 -Env dev -App ConstFlow
.\scripts\deploy-env.ps1 -Env dev -App ProFlow
.\scripts\deploy-env.ps1 -Env dev -App WaterFlow

# Tear down one app (shared network is kept)
.\scripts\destroy-env.ps1 -Env dev -App ConstFlow

# Tear everything down (RDS, Fargate, CloudFront, VPC)
.\scripts\destroy-env.ps1 -Env dev
```

Equivalent CDK (`npx cdk` uses the version in `package.json`; a global `cdk` works if it is 2.1000+):

```powershell
npx cdk deploy --all -c env=dev
npx cdk deploy --all -c env=dev -c app=ConstFlow
npx cdk destroy AppPlatform-Dev-ConstFlow -c env=dev -c app=ConstFlow --force
npx cdk destroy --all -c env=dev --force
```

First deploy takes a while (VPC, NAT, RDS, CloudFront). Destroy is similarly slow because CloudFront distributions take time to delete.

## How routing works

There is no custom domain in this first version.

| App | API | Frontend |
| --- | --- | --- |
| ConstFlow | `http://<alb-dns>/constflow` | CloudFront URL from stack output `FrontendUrl` |
| ProFlow | `http://<alb-dns>/proflow` | CloudFront URL from stack output `FrontendUrl` |
| WaterFlow | `http://<alb-dns>/waterflow` | CloudFront URL from stack output `FrontendUrl` |

`ASPNETCORE_PATHBASE` is already injected as `/{app}`. React production builds should use that API URL (for example `VITE_API_URL=http://<alb-dns>/constflow`).

## Database connection

The API task receives:

- `DB_HOST`, `DB_PORT`, `DB_NAME` (environment)
- `DB_USER`, `DB_PASSWORD` (Secrets Manager)

Build a connection string in the API, for example:

`Host={DB_HOST};Port={DB_PORT};Database={DB_NAME};Username={DB_USER};Password={DB_PASSWORD}`

RDS is in private subnets and only accepts connections from that app's Fargate security group.

## Container images

Until an app repo pushes a real image, each service runs `public.ecr.aws/nginx/nginx:stable-alpine` (port 80, health check `/`). Each app stack still creates an ECR repository.

After you push an image:

```powershell
npx cdk deploy --all -c env=dev -c app=ConstFlow -c ConstFlowImage=<account>.dkr.ecr.us-west-1.amazonaws.com/app-platform/dev/constflow:tag
```

Real ASP.NET 8 images should listen on **8080** and expose **GET /health**. Those values are already set on the app definition; they take effect when `ImageUri` is set (placeholder nginx uses 80 and `/`).

## Adding another app

Add an entry in [`src/AppPlatform.Infra/Configuration/AppDefinitions.cs`](src/AppPlatform.Infra/Configuration/AppDefinitions.cs):

```csharp
new("ThirdApp", ContainerPort: 8080, HealthPath: "/health", Cpu: 256, MemoryMiB: 512, ListenerPriority: 40),
```

`ListenerPriority` must be unique. Add the same name to the `app` choices in `.github/workflows/deploy-env.yml` and `destroy-env.yml`. Redeploy with `-App ThirdApp` or omit `-App` to deploy every app.

## GitHub Actions

Manual workflows:

- **Deploy environment** — `workflow_dispatch` with `dev` or `test`, and `all` / `ConstFlow` / `ProFlow` / `WaterFlow`
- **Destroy environment** — same. `all` tears down the whole environment; a single app leaves the shared network in place

Create an IAM role that GitHub can assume via OIDC, then add:

| Name | Type | Value |
| --- | --- | --- |
| `AWS_ROLE_ARN` | repository secret | IAM role ARN |
| `AWS_REGION` | repository variable (optional) | default `us-west-1` |

Trust GitHub OIDC (`token.actions.githubusercontent.com`) for this repo (`Dale4/app-platform`). The role needs permissions to deploy CDK stacks (AdministratorAccess is simplest for a sandbox; tighten later).

The account must already be `cdk bootstrap`ped.

## Cost

Leaving one environment up with three apps is roughly **$110–140/month** (NAT, ALB, three `db.t4g.micro` instances, Fargate). Destroy the environment when you are not using it.

Non-prod resources use `RemovalPolicy.DESTROY`, RDS deletion protection off, S3 auto-delete, and ECR empty-on-delete so destroy completes without leftover buckets or images.
