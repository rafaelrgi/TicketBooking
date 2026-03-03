#!/bin/bash

# Create table
awslocal dynamodb create-table \
    --table-name Tickets \
    --attribute-definitions AttributeName=PK,AttributeType=S AttributeName=SK,AttributeType=S \
    --key-schema AttributeName=PK,KeyType=HASH AttributeName=SK,KeyType=RANGE \
    --billing-mode PAY_PER_REQUEST \
    --stream-specification StreamEnabled=true,StreamViewType=NEW_AND_OLD_IMAGES

# Create SQS Queue 
awslocal sqs create-queue --queue-name TicketUpdatesQueue

# Reservation State Machine
awslocal stepfunctions delete-state-machine \
    --state-machine-arn arn:aws:states:us-east-1:000000000000:stateMachine:TicketBookingWorkflow

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
        "UpdateExpression": "SET #s = :cancelled",
        "ExpressionAttributeNames": { "#s": "Status" },
        "ExpressionAttributeValues": { ":cancelled": { "S": "Cancelled" } }
      },
      "Next": "NotifyQueue"
    },
    "NotifyQueue": {
      "Type": "Task",
      "Resource": "arn:aws:states:::sqs:sendMessage",
      "Parameters": {
        "QueueUrl": "http://localhost:4566/000000000000/TicketUpdatesQueue",
        "MessageBody": {
          "PK.$": "$.PK",
          "SK.$": "$.SK",
          "Status": "Cancelled"
        }
      },
      "End": true
    }
  }
}'

awslocal stepfunctions create-state-machine \
    --name "TicketBookingWorkflow" \
    --definition "$DEFINITION" \
    --role-arn "arn:aws:iam::000000000000:role/stepfunctions-role"
