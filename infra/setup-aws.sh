#!/bin/bash

# Create tables
awslocal dynamodb create-table \
    --table-name Events \
    --attribute-definitions AttributeName=PK,AttributeType=S \
    --key-schema AttributeName=PK,KeyType=HASH \
    --billing-mode PAY_PER_REQUEST  || true
    # PK: Event#id | SK: Metadata
    # Attributes: EventId, TotalTickets

awslocal dynamodb create-table \
    --table-name Tickets \
    --attribute-definitions AttributeName=PK,AttributeType=S AttributeName=SK,AttributeType=S \
    --key-schema AttributeName=PK,KeyType=HASH AttributeName=SK,KeyType=RANGE \
    --billing-mode PAY_PER_REQUEST  || true
    # PK: Event#id | Ticket#id
    # Attributes: Status (Reserved/Confirmed), UserId, IsVip

# Create SQS Queue 
awslocal sqs create-queue --queue-name TicketUpdatesQueue || true

# Reservation State Machine
# awslocal stepfunctions delete-state-machine --state-machine-arn arn:aws:states:sa-east-1:000000000000:stateMachine:TicketBookingWorkflow || true

DEFINITION_FILE="/etc/localstack/init/ready.d/workflow-definition.json"

if [ -f "$DEFINITION_FILE" ]; then
    echo "Reading State Machine from $DEFINITION_FILE"
    DEFINITION=$(cat "$DEFINITION_FILE")
else
    echo "ERROR: missing State Machine definition! $DEFINITION_FILE"
    exit 1
fi

awslocal stepfunctions create-state-machine \
    --region sa-east-1 \
    --name "TicketBookingWorkflow" \
    --definition "$DEFINITION" \
    --role-arn "arn:aws:iam::000000000000:role/stepfunctions-role"  || true
