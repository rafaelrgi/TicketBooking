x Renomear "seat" >> "ticket"

x Testes

- Combo select evento

- Service & Dto

- Url's em config

- Log

- Grafana LGTM Stack (O "Padrão Ouro" da indústria)
Se você quer algo que as empresas realmente usam em produção (inclusive no Horizon/Portside, possivelmente), aprenda a LGTM Stack.

Loki (Logs)

Grafana (Dashboards)

Tempo (Traces)

Mimir (Métricas)

A dica de ouro (Docker "all-in-one"):
A própria Grafana Labs mantém uma imagem Docker chamada grafana/otel-lgtm que sobe a stack inteira pronta para receber dados de OpenTelemetry em segundos.

Bash
docker run -p 3000:3000 -p 4317:4317 -p 4318:4318 grafana/otel-lgtm

------------------------------------------------------------------------------------------------------------------------

http://localhost:5025

// docker compose manual
docker run -d --name ticket-booking-aws \
  -p 4566:4566 -p 4510-4559:4510-4559 \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v $(pwd)/infra:/etc/localstack/init/ready.d \
  -e SERVICES=dynamodb,stepfunctions,sts,iam \
  -e AWS_DEFAULT_REGION=us-east-1 \
  localstack/localstack

// Rodar Api  no Terminal
dotnet run --project src/TicketBooking.Api/TicketBooking.Api.csproj --launch-profile http

// Rodar Admin  no Terminal
dotnet run --project src/TicketBooking.Admin/TicketBooking.Admin.csproj

// Rodar script setup no container
docker exec -it ticket-booking-aws bash /etc/localstack/init/ready.d/setup-aws.sh

// View table
docker exec -it ticket-booking-aws awslocal dynamodb scan --table-name Tickets

// Delete table
docker exec -it ticket-booking-aws awslocal dynamodb delete-table --table-name Tickets

// Delete State Machine
docker exec -it ticket-booking-aws awslocal stepfunctions delete-state-machine --state-machine-arn "arn:aws:states:us-east-1:000000000000:stateMachine:TicketBookingWorkflow"

// Log LocalStack
docker logs -f ticket-booking-aws

// Listar todas as execuções da State Machine:
docker exec -it ticket-booking-aws awslocal stepfunctions list-executions --state-machine-arn "arn:aws:states:us-east-1:000000000000:stateMachine:TicketBookingWorkflow"

// Ver Status de uma Execução Específica
docker exec -it ticket-booking-aws awslocal stepfunctions describe-execution --execution-arn "COLE_AQUI_O_ARN_RETORNADO"

// update na tabela
docker exec -it ticket-booking-aws awslocal dynamodb update-item \
    --table-name Tickets \
    --key '{"PK": {"S": "EVENT#show-do-ano"}, "SK": {"S": "TICKET#A1-VIP"}}' \
    --update-expression "SET #s = :val" \
    --expression-attribute-names '{"#s": "Status"}' \
    --expression-attribute-values '{":val": {"S": "Confirmed"}}'

docker exec -it ticket-booking-aws awslocal dynamodb update-item \
    --table-name Seats \
    --key '{"PK": {"S": "EVENT#show-do-ano"}, "SK": {"S": "TICKET#A1-VIP"}}' \
    --update-expression "SET #s = :val" \
    --expression-attribute-names '{"#s": "Status"}' \
    --expression-attribute-values '{":val": {"S": "Reserved"}}'

