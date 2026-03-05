------------------------------------------------------------------------------------------------------------------------

http://localhost:5025

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

