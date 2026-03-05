#!/bin/bash

# Create table
awslocal dynamodb create-table \
    --table-name Tickets \
    --attribute-definitions AttributeName=PK,AttributeType=S AttributeName=SK,AttributeType=S \
    --key-schema AttributeName=PK,KeyType=HASH AttributeName=SK,KeyType=RANGE \
    --billing-mode PAY_PER_REQUEST || true

# Create SQS Queue 
awslocal sqs create-queue --queue-name TicketUpdatesQueue || true

# Reservation State Machine
awslocal stepfunctions delete-state-machine \
    --state-machine-arn arn:aws:states:sa-east-1:000000000000:stateMachine:TicketBookingWorkflow || true

DEFINITION='{
  "StartAt": "WaitForPayment",
  "States": {
    "WaitForPayment": {
      "Type": "Wait",
      "Seconds": 12,
      "Next": "CancelReservation"
    },
    "CancelReservation": {
      "Type": "Task",
      "Resource": "arn:aws:states:::dynamodb:updateItem",
      "ResultPath": "$.updateResult",
      "Parameters": {
        "TableName": "Tickets",
        "Key": {
          "PK": { "S.$": "$.PK" },
          "SK": { "S.$": "$.SK" }
        },
        "UpdateExpression": "SET #s = :available, UserId = :empty, UpdatedAt = :now",
        "ConditionExpression": "#s <> :confirmed",
        "ExpressionAttributeNames": { "#s": "Status" },
        "ExpressionAttributeValues": {
          ":available": { "S": "Available" },
          ":confirmed": { "S": "Confirmed" },
          ":empty": { "S": "" },
          ":now": { "S.$": "$$.State.EnteredTime" }
        }
      },
      "Catch": [
        {
          "ErrorEquals": ["DynamoDb.ConditionalCheckFailedException"],
          "Next": "IgnoreCancellation"
        }
      ],
      "Next": "NotifyQueue"
    },
    "IgnoreCancellation": {
      "Type": "Pass",
      "Result": "Ticket was already confirmed, skipping cancellation.",
      "End": true
    },
    "NotifyQueue": {
      "Type": "Task",
      "Resource": "arn:aws:states:::sqs:sendMessage",
      "Parameters": {
        "QueueUrl": "http://sqs.sa-east-1.localhost.localstack.cloud:4566/000000000000/TicketUpdatesQueue",
        "MessageBody": {
          "PK.$": "$.PK",
          "SK.$": "$.SK",
          "Status": "Available"
        }
      },
      "End": true
    }
  }
}'

awslocal stepfunctions create-state-machine \
    --name "TicketBookingWorkflow" \
    --definition "$DEFINITION" \
    --role-arn "arn:aws:iam::000000000000:role/stepfunctions-role"  || true
