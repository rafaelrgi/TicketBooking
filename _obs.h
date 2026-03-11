x Renomear "seat" >> "ticket"

x Testes

x Combo select evento

x Confirmação pgto

x Redis

x Só salvar tickets reservados ou confirmados, disponiveis controlar pela tabela Events

x Regras de negócio:
  x Só cancelar tickets não confirmados
  x Não reservar tickets acima da quota
  x Não reservar tickets se evento não existe
  x Só confirmar tickets reservados
  x Só reservar tickets não confirmados e não reservados

x Auth (KeyCloak)

- Urls

- Log

- API Gateway

- CloudWatch

- EventBridge

- SNS

- Readme

- Url's em config

- Grafana ?

------------------------------------------------------------------------------------------------------------------------

http://localhost:5025

// Rodar no Terminal
dotnet run --project src/TicketBooking.Api/TicketBooking.Api.csproj --launch-profile http
dotnet run --project src/TicketBooking.Admin/TicketBooking.Admin.csproj

// Rodar script setup no container
docker exec -it ticket-booking-aws bash /etc/localstack/init/ready.d/setup-aws.sh

// View table
docker exec -it ticket-booking-aws awslocal dynamodb scan --table-name Tickets
docker exec -it ticket-booking-aws awslocal dynamodb scan --table-name Events

// Delete table
docker exec -it ticket-booking-aws awslocal dynamodb delete-table --table-name Tickets

// Delete State Machine
docker exec -it ticket-booking-aws awslocal stepfunctions delete-state-machine --state-machine-arn "arn:aws:states:sa-east-1:000000000000:stateMachine:TicketBookingWorkflow"

// Log LocalStack
docker logs -f ticket-booking-aws

// Investigar erros State Machine:
docker exec -it ticket-booking-aws awslocal stepfunctions list-state-machines
docker exec -it ticket-booking-aws awslocal stepfunctions list-executions --state-machine-arn "arn:aws:states:sa-east-1:000000000000:stateMachine:TicketBookingWorkflow"
docker exec -it ticket-booking-aws awslocal stepfunctions get-execution-history --execution-arn <ARN RETORNADO ACIMA>

// Ver Status de uma Execução Específica
docker exec -it ticket-booking-aws awslocal stepfunctions describe-execution --execution-arn "COLE_AQUI_O_ARN_RETORNADO"

// update na tabela
docker exec -it ticket-booking-aws awslocal dynamodb update-item \
    --table-name Tickets \
    --key '{"PK": {"S": "EVENT#rock-in-rio"}, "SK": {"S": "TICKET#A1-VIP"}}' \
    --update-expression "SET #s = :val" \
    --expression-attribute-names '{"#s": "Status"}' \
    --expression-attribute-values '{":val": {"S": "Confirmed"}}'

docker exec -it ticket-booking-aws awslocal dynamodb update-item \
    --table-name Seats \
    --key '{"PK": {"S": "EVENT#rock-in-rio"}, "SK": {"S": "TICKET#A1-VIP"}}' \
    --update-expression "SET #s = :val" \
    --expression-attribute-names '{"#s": "Status"}' \
    --expression-attribute-values '{":val": {"S": "Reserved"}}'

